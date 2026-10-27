using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Transactions;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace VF_CR_Management_System.Business.ChangeRequestHandler
{
    public class ChangeRequestService : IChangeRequestService
    {
        private readonly _ConnectionService _connectionService;
        private readonly string _attachmentRootFolder;

        public ChangeRequestService(_ConnectionService connectionService, IConfiguration configuration)
        {
            _connectionService = connectionService;
            _attachmentRootFolder = configuration["AttachmentSettings:RootFolder"]
                ?? throw new InvalidOperationException("AttachmentSettings:RootFolder is not configured.");

        }
        public async Task<int> CreateChangeRequestAsync(IFormCollection collection, string empId)
        {
            if (!int.TryParse(collection["StatusID"], out var statusId))
            {
                throw new ArgumentException("Missing or invalid status.");
            }

            bool isSubmit = statusId == 2;

            // ---- Parse everything leniently first ----
            int.TryParse(collection["ChangeTypeID"], out var changeTypeId);
            int.TryParse(collection["PriorityID"], out var priorityId);
            var changeTitle = collection["ChangeTitle"].ToString();
            var summary = collection["Summary"].ToString();
            var otherChangeType = collection["OtherChangeType"].ToString();
            int.TryParse(collection["DivisionID"], out var divisionId);
            int.TryParse(collection["ModuleID"], out var moduleId);

            var approverIdRaw = collection["ApproverID"].ToString();
            int approverId = 0;
            bool hasApprover = int.TryParse(approverIdRaw, out approverId);

            // ---- Full validation only applies when actually submitting ----
            if (isSubmit)
            {
                if (changeTypeId <= 0)
                    throw new ArgumentException("Please select a change type.");
                if (priorityId <= 0)
                    throw new ArgumentException("Please select a change priority.");
                if (string.IsNullOrWhiteSpace(changeTitle))
                    throw new ArgumentException("Please provide a Change Title");
                if (string.IsNullOrWhiteSpace(summary))
                    throw new ArgumentException("Please provide a change summary and business justification.");
                if (changeTypeId == 5 && string.IsNullOrWhiteSpace(otherChangeType))
                    throw new ArgumentException("Please specify the change type.");
                if (divisionId <= 0)
                    throw new ArgumentException("Please select a Division.");
                if (moduleId <= 0)
                    throw new ArgumentException("Please select a Module.");
                if (!hasApprover)
                    throw new ArgumentException("Please select a Approver.");
            }

            const string crSql = @"
                INSERT INTO ChangeRequest
                    (CRNumber, RequesterUserName, ChangeTitle, Summary, ChangeTypeID, OtherType, PriorityID, DivisionID, ModuleID, 
                     RequestedDate, StatusID, Active)
                VALUES
                    (@CRNumber, @RequesterUserName, @ChangeTitle, @Summary, @ChangeTypeID, @OtherType, @PriorityID, @DivisionID, @ModuleID,
                     @RequestedDate, @StatusID, @Active);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            const int maxAttempts = 5;
            int newCrId = 0;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var crNumber = statusId == 2
                    ? GenerateNextCrNumber()
                    : $"Waiting-{Guid.NewGuid():N}".Substring(0, 16);

                var crParameters = new DynamicParameters();
                crParameters.Add("@CRNumber", crNumber);
                crParameters.Add("@RequesterUserName", empId);
                crParameters.Add("@ChangeTitle", changeTitle);
                crParameters.Add("@Summary", summary);
                crParameters.Add("@ChangeTypeID", changeTypeId > 0 ? (int?)changeTypeId : null);
                crParameters.Add("@OtherType", otherChangeType);
                crParameters.Add("@PriorityID", priorityId > 0 ? (int?)priorityId : null);
                crParameters.Add("@ModuleID", moduleId > 0 ? (int?)moduleId : null);
                crParameters.Add("@DivisionID", divisionId > 0 ? (int?)divisionId : null);
                crParameters.Add("@RequestedDate", DateTime.Now);
                crParameters.Add("@StatusID", statusId);
                crParameters.Add("@Active", true);

                try
                {
                    var scalarResult = _connectionService.ExecuteScalar(crSql, crParameters);
                    newCrId = scalarResult != null ? Convert.ToInt32(scalarResult) : 0;
                    break; // success
                }
                catch (Exception ex) when (attempt < maxAttempts && IsDuplicateCrNumberError(ex))
                {
                    continue;
                }
            }

            if (newCrId <= 0)
                return 0;

            if (hasApprover)
            {
                // 1. Fetch the StepID dynamically based on the StepName
                const string getStepIdSql = @"
                SELECT TOP 1 StepID 
                FROM WorkflowStep 
                WHERE StepName LIKE '%Division Head Approval%' AND Active = 1
                ORDER BY StepOrder ASC;";

                // Use ExecuteScalar or similar helper method from your connection service
                var stepIdObj = _connectionService.ExecuteScalar(getStepIdSql);

                if (stepIdObj == null || stepIdObj == DBNull.Value)
                {
                    throw new InvalidOperationException("Workflow step for 'Division Head' approval was not found or is inactive.");
                }

                int targetStepId = Convert.ToInt32(stepIdObj);

                // 2. Insert into Approval table using the dynamically fetched StepID
                const string approvalSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active);";

                var approvalParameters = new DynamicParameters();
                approvalParameters.Add("@CRID", newCrId);
                approvalParameters.Add("@StepID", targetStepId);
                approvalParameters.Add("@AssignedBy", empId);
                approvalParameters.Add("@AssignedTo", approverId);
                approvalParameters.Add("@AssignedDate", DateTime.Now);
                approvalParameters.Add("@Active", true);

                int approvalRowsAffected = _connectionService.ExecuteWithPara(approvalSql, approvalParameters);

                return approvalRowsAffected > 0 ? newCrId : 0;
            }

            return newCrId;
        }


        public async Task<ChangeRequest> GetChangeRequestByIdAsync(int crId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            const string sql = @"
                SELECT
                    cr.CRID,
                    cr.CRNumber,
                    cr.Summary,
                    cr.ChangeTitle,
                    cr.ChangeTypeID,
                    cr.OtherType AS OtherChangeType,
                    cr.ActivitiesTasks,
                    cr.FixedAssets,
                    cr.VendorID,
                    cr.ProposalNumber,
                    cr.RiskAssessment,
                    cr.SecurityRequirements,
                    cr.SecurityRisksIdentified,
                    cr.RiskTreatmentControls,
                    cr.DependenciesImpact,
                    cr.ImpactID,
                    ci.ImpactName     AS ChangeImpact,
                    ct.ChangeTypeName AS ChangeType,
                    cr.PriorityID,
                    p.PriorityName    AS Priority,
                    cr.DivisionID,
                    dv.DivisionName   AS Division,
                    cr.ModuleID,
                    m.ModuleName      AS Module,
                    cr.StatusID,
                    s.StatusName      AS Status,
                    cr.WorkflowID,
                    cr.RequesterUserName AS RequestedBy,
                    cr.RequestedDate,
                    cr.DueDate
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType]   ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority]     p  ON p.PriorityID   = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division]     dv ON dv.DivisionID  = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module]       m  ON m.ModuleID     = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus]     s  ON s.StatusID     = cr.StatusID
                LEFT JOIN [CRManagementDB].[dbo].[ChangeImpact] ci ON ci.ImpactID    = cr.ImpactID
                WHERE cr.Active = 1
                  AND cr.CRID = @CRID";

            var result = _connectionService.Query<ChangeRequest>(sql, new { CRID = crId });
            var changeRequest = result.FirstOrDefault();

            if (changeRequest == null)
                return null;

            const string stepsQuery = @"
                    SELECT  
                        A.ApprovalID,
                        WS.StepID,
                        WS.StepOrder,
                        WS.StepName,
                        WS.ApprovalRequired,
                        A.AssignedBy,
                        A.AssignedTo,
                        A.AssignedDate,
                        A.TargetDate,
                        A.Decision,
                        A.ApprovalDate,
                        A.IsApproved
                    FROM Approval A
                    INNER JOIN [WorkflowStep] WS ON WS.StepID = A.StepID
                    INNER JOIN ChangeRequest CR ON CR.CRID = A.CRID
                    WHERE A.CRID = @CRID
                      AND A.Active = 1
                   ORDER BY
                    CASE WHEN A.ApprovalDate IS NULL THEN 1 ELSE 0 END,
                    A.ApprovalDate,
                    A.ApprovalID";

            var wflow = changeRequest.WorkflowID > 0 ? changeRequest.WorkflowID : 3;

            var stepParams = new DynamicParameters();
            stepParams.Add("@CRID", crId);

            var stepsTable = _connectionService.ReturnWithPara(stepsQuery, stepParams);

            changeRequest.StepAssignments = stepsTable.AsEnumerable()
                .Select(r => new WorkflowStepAssignment
                {
                    StepID = r.Field<int>("StepID"),
                    StepOrder = r.Field<int>("StepOrder"),
                    StepName = r.Field<string>("StepName"),
                    ApprovalRequired = r.Field<bool>("ApprovalRequired"),
                    AssignedBy = r.Field<string>("AssignedBy"),
                    AssignedTo = r.Field<string>("AssignedTo"),
                    AssignedDate = r.Field<DateTime?>("AssignedDate"),
                    TargetDate = r.Field<DateTime?>("TargetDate"),
                    Decision = r.Field<string>("Decision"),
                    ApprovalDate = r.Field<DateTime?>("ApprovalDate"),   // <-- the fix: now mapped
                    IsApproved = r.Field<bool?>("IsApproved")
                })
                .ToList();

            // ---------- Resolve full names (requester + every assigned user) ----------
            var userNames = changeRequest.StepAssignments
                .Select(x => x.AssignedTo)
                .Append(changeRequest.RequestedBy)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
                    SELECT UserName, FirstName, LastName
                    FROM Users
                    WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .GroupBy(r => r.Field<string>("UserName"), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g =>
                        {
                            var r = g.First();
                            return $"{r.Field<string>("FirstName")} {r.Field<string>("LastName")}".Trim();
                        },
                        StringComparer.OrdinalIgnoreCase);

                string ResolveName(string userName) =>
                    !string.IsNullOrWhiteSpace(userName) &&
                    nameLookup.TryGetValue(userName, out var full) &&
                    !string.IsNullOrWhiteSpace(full)
                        ? full
                        : userName;

                changeRequest.RequestedBy = ResolveName(changeRequest.RequestedBy);

                foreach (var step in changeRequest.StepAssignments)
                    step.AssignedToName = ResolveName(step.AssignedTo);
            }

            // ---------- Map each workflow step to its named property (by StepName) ----------
            string Key(string s) => (s ?? string.Empty).Replace(" ", string.Empty);

            string GetAssigneeByStepName(string stepName) =>
                changeRequest.StepAssignments
                    .FirstOrDefault(s => string.Equals(Key(s.StepName), Key(stepName), StringComparison.OrdinalIgnoreCase))
                    ?.AssignedToName;

            changeRequest.ApproverUserName = GetAssigneeByStepName("Division Head Approval");
            changeRequest.AssessmentUserName = new[] { "Developer Documentation" }
             .Select(GetAssigneeByStepName)
             .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            changeRequest.SecurityUserName = GetAssigneeByStepName("Security");
            changeRequest.TestingUserName = GetAssigneeByStepName("Testing");
            changeRequest.FinalApproverUserName = GetAssigneeByStepName("Final Approval");
            changeRequest.TesterUserName = GetAssigneeByStepName("TesterAssignment");
            changeRequest.TestingApproverUserName = GetAssigneeByStepName("TestingApproval");

            // ---------- Testing records ----------
            const string testingQuery = @"
                SELECT TestID, CRID, TestCycleNumber, TestResult, UATLink, TestingDate, IsPassed, ReturnedFrom, Active
                FROM [CRManagementDB].[dbo].[Testing]
                WHERE CRID = @CRID AND Active = 1
                ORDER BY TestCycleNumber DESC";

            var testingParams = new DynamicParameters();
            testingParams.Add("@CRID", crId);

            var testingTable = _connectionService.ReturnWithPara(testingQuery, testingParams);

            changeRequest.Tests = testingTable.AsEnumerable()
                .Select(r => new Testing
                {
                    TestID = r.Field<int>("TestID"),
                    CRID = r.Field<int>("CRID"),
                    TestCycleNumber = r.Field<int>("TestCycleNumber"),
                    TestResult = r.Field<string>("TestResult"),
                    UATLink = r.Field<string>("UATLink"),
                    TestingDate = r.Field<DateTime?>("TestingDate"),
                    IsPassed = r.Field<bool?>("IsPassed"),
                    ReturnedFrom = r.Field<string>("ReturnedFrom"),
                    Active = r.Field<bool>("Active")
                }).ToList();

            return changeRequest;
        }

        public Task<IEnumerable<Attachment>> GetAttachmentsByCrIdAsync(int crId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            const string sql = @"
                SELECT
                    a.AttachmentID,
                    a.CRID,
                    a.FileName,
                    a.FilePath,
                    a.UploadedBy,
                    a.UploadedDate,
                    a.Active,
                    t.AttachmentName AS AttachmentTypeName
                FROM [CRManagementDB].[dbo].[Attachment] a
                LEFT JOIN [CRManagementDB].[dbo].[AttachmentType] t
                    ON t.AttachmentTypeID = a.AttachmentTypeID
                WHERE a.CRID = @CRID
                  AND a.Active = 1
                ORDER BY a.UploadedDate DESC";

            var result = _connectionService.Query<Attachment>(sql, new { CRID = crId })
                            ?? Enumerable.Empty<Attachment>();

            return Task.FromResult(result);
        }

        public Task<Attachment> GetAttachmentByIdAsync(int attachmentId)
        {
            if (attachmentId <= 0)
                throw new ArgumentException("Invalid attachment.");

            const string sql = @"
                SELECT
                    a.AttachmentID,
                    a.CRID,
                    a.FileName,
                    a.FilePath,
                    a.UploadedBy,
                    a.UploadedDate,
                    a.Active,
                    t.AttachmentName AS AttachmentTypeName
                FROM [CRManagementDB].[dbo].[Attachment] a
                LEFT JOIN [CRManagementDB].[dbo].[AttachmentType] t
                    ON t.AttachmentTypeID = a.AttachmentTypeID
                WHERE a.AttachmentID = @AttachmentID
                  AND a.Active = 1";

            var result = _connectionService.Query<Attachment>(sql, new { AttachmentID = attachmentId });
            var attachment = result?.FirstOrDefault();

            return Task.FromResult(attachment);
        }

        public async Task<string> GetAssignedApproverUserNameAsync(int crId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            const int assignStepId = 7;

            const string sql = @"
                SELECT TOP 1 AssignedTo
                FROM [CRManagementDB].[dbo].[Approval]
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND Active = 1
                ORDER BY AssignedDate DESC";

            var result = _connectionService.ExecuteScalar(sql, new { CRID = crId, StepID = assignStepId });
            return result != null && result != DBNull.Value ? result.ToString() : null;
        }

        public async Task<bool> UpdateChangeRequestAsync(int crId, IFormCollection collection, string userName, string empId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            if (!int.TryParse(collection["StatusID"], out var incomingStatusId))
            {
                incomingStatusId = 1; // default to draft if not present
            }

            bool isSubmit = incomingStatusId == 2;

            int.TryParse(collection["ChangeTypeID"], out var changeTypeId);
            int.TryParse(collection["PriorityID"], out var priorityId);
            var changeTitle = collection["ChangeTitle"].ToString();
            var summary = collection["Summary"].ToString();
            var otherChangeType = collection["OtherChangeType"].ToString();
            int.TryParse(collection["DivisionID"], out var divisionId);
            int.TryParse(collection["ModuleID"], out var moduleId);

            var approverIdRaw = collection["ApproverID"].ToString();
            int approverId = 0;
            bool hasApprover = int.TryParse(approverIdRaw, out approverId);

            if (isSubmit)
            {
                if (changeTypeId <= 0)
                    throw new ArgumentException("Please select a change type.");
                if (priorityId <= 0)
                    throw new ArgumentException("Please select a change priority.");
                if (string.IsNullOrWhiteSpace(changeTitle))
                    throw new ArgumentException("Please provide a Change Request Title");
                if (string.IsNullOrWhiteSpace(summary))
                    throw new ArgumentException("Please provide a change summary and business justification.");
                if (changeTypeId == 5 && string.IsNullOrWhiteSpace(otherChangeType))
                    throw new ArgumentException("Please specify the change type.");
                if (divisionId <= 0)
                    throw new ArgumentException("Please select a DIvision.");
                if (moduleId <= 0)
                    throw new ArgumentException("Please select a Module.");
                if (!hasApprover)
                    throw new ArgumentException("Please select a Approver.");
            }

            const int draftStatusId = 1;

            const string updateCrSql = @"
                UPDATE ChangeRequest
                SET Summary      = @Summary,
                    ChangeTitle  = @ChangeTitle,
                    ChangeTypeID = @ChangeTypeID,
                    OtherType    = @OtherType,
                    PriorityID   = @PriorityID,
                    ModuleID     = @ModuleID,
                    DivisionID     = @DivisionID,
                    StatusID     = @StatusID
                WHERE CRID = @CRID
                  AND Active = 1
                  AND CRNumber LIKE 'Waiting-%'";

            var crParameters = new DynamicParameters();
            crParameters.Add("@Summary", summary);
            crParameters.Add("@ChangeTitle", changeTitle);
            crParameters.Add("@ChangeTypeID", changeTypeId > 0 ? (int?)changeTypeId : null);
            crParameters.Add("@OtherType", otherChangeType);
            crParameters.Add("@PriorityID", priorityId > 0 ? (int?)priorityId : null);
            crParameters.Add("@DivisionID", divisionId > 0 ? (int?)divisionId : null);
            crParameters.Add("@ModuleID", moduleId > 0 ? (int?)moduleId : null);
            crParameters.Add("@StatusID", draftStatusId);
            crParameters.Add("@CRID", crId);

            int crRowsAffected = _connectionService.ExecuteWithPara(updateCrSql, crParameters);

            if (crRowsAffected <= 0)
                return false;

            if (hasApprover)
            {
                const string getStepIdSql = @"
                    SELECT TOP 1 StepID 
                    FROM WorkflowStep 
                    WHERE StepName LIKE '%Division Head Approval%' AND Active = 1
                    ORDER BY StepOrder ASC;";

                var stepIdObj = _connectionService.ExecuteScalar(getStepIdSql);

                if (stepIdObj == null || stepIdObj == DBNull.Value)
                {
                    throw new InvalidOperationException("Workflow step for 'Division Head' approval was not found or is inactive.");
                }

                int assignStepId = Convert.ToInt32(stepIdObj);

                // 2. Attempt to update existing active record
                const string updateApprovalSql = @"
                    UPDATE Approval
                    SET AssignedBy   = @AssignedBy,
                        AssignedTo   = @AssignedTo,
                        AssignedDate = @AssignedDate
                    WHERE CRID = @CRID
                      AND StepID = @StepID
                      AND Active = 1";

                var approvalParameters = new DynamicParameters();
                approvalParameters.Add("@AssignedBy", empId);
                approvalParameters.Add("@AssignedTo", approverId);
                approvalParameters.Add("@AssignedDate", DateTime.Now);
                approvalParameters.Add("@CRID", crId);
                approvalParameters.Add("@StepID", assignStepId);

                int approvalRowsAffected = _connectionService.ExecuteWithPara(updateApprovalSql, approvalParameters);

                // 3. Fallback to insert if no record was updated
                if (approvalRowsAffected <= 0)
                {
                    const string insertApprovalSql = @"
                        INSERT INTO Approval
                            (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                        VALUES
                            (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

                    var insertParameters = new DynamicParameters();
                    insertParameters.Add("@CRID", crId);
                    insertParameters.Add("@StepID", assignStepId);
                    insertParameters.Add("@AssignedBy", empId);
                    insertParameters.Add("@AssignedTo", approverId);
                    insertParameters.Add("@AssignedDate", DateTime.Now);
                    insertParameters.Add("@Active", true);

                    _connectionService.ExecuteWithPara(insertApprovalSql, insertParameters);
                }
            }

            return true;
        }
        private static bool IsDuplicateCrNumberError(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
                    return true;

                if (current.Message != null &&
                    current.Message.IndexOf("UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private string GenerateNextCrNumber()
        {
            var now = DateTime.Now;
            var year = now.Year;
            var month = now.Month;
            var prefix = $"CR/{year}/{month:D2}/";

            const string maxSql = @"
                SELECT MAX(CAST(RIGHT(CRNumber, 3) AS INT))
                FROM ChangeRequest
                WHERE CRNumber LIKE @Prefix + '%'";

            var result = _connectionService.ExecuteScalar(maxSql, new { Prefix = prefix });
            var lastNumber = (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;

            return $"{prefix}{(lastNumber + 1):D3}";   // <-- D3, not D5
        }

        public async Task<bool> ApproveChangeRequestAsync(int crId, int approverId, int testerId, string approvedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");
            if (approverId <= 0)
                throw new ArgumentException("Please select a valid user to assign.");
            if (testerId <= 0)
                throw new ArgumentException("Please select a valid QA user to assign.");

            const string updateApprovalSql = @"
                UPDATE Approval
                SET IsApproved = 1,
                    ApprovalDate = @ApprovalDate,
                    Decision = 'Division Head Approved'
                WHERE CRID = @CRID
                  AND Active = 1";

            var updateApprovalParameters = new DynamicParameters();
            updateApprovalParameters.Add("@CRID", crId);
            updateApprovalParameters.Add("@ApprovalDate", DateTime.Now);

            int approvalRowsAffected = _connectionService.ExecuteWithPara(updateApprovalSql, updateApprovalParameters);

            if (approvalRowsAffected <= 0)
                return false;

            const string getStatusIdSql = @"
                SELECT TOP 1 StatusID
                FROM [CRManagementDB].[dbo].[CRStatus]
                WHERE StatusName LIKE '%Approved%' AND Active = 1";

            var statusIdObj = _connectionService.ExecuteScalar(getStatusIdSql);

            if (statusIdObj == null || statusIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Status 'Approved' was not found or is inactive in CRStatus table.");
            }

            int approvedStatusId = Convert.ToInt32(statusIdObj);

            const string updateStatusSql = @"
                UPDATE ChangeRequest
                SET StatusID = @StatusID
                WHERE CRID = @CRID";

            var statusParameters = new DynamicParameters();
            statusParameters.Add("@StatusID", approvedStatusId);
            statusParameters.Add("@CRID", crId);

            int statusRowsAffected = _connectionService.ExecuteWithPara(updateStatusSql, statusParameters);

            if (statusRowsAffected <= 0)
                return false;

            const string getAssessmentStepIdSql = @"
                SELECT TOP 1 StepID
                FROM [CRManagementDB].[dbo].[WorkflowStep]
                WHERE StepName LIKE '%Developer Documentation%' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getAssessmentStepIdSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step for 'Developer Documentation' was not found or is inactive.");
            }

            int assessmentStepId = Convert.ToInt32(stepIdObj);

            const string getTestingStepIdSql = @"
                SELECT TOP 1 StepID
                FROM [CRManagementDB].[dbo].[WorkflowStep]
                WHERE StepName = 'Testing' AND Active = 1
                ORDER BY StepOrder ASC";

            var testingStepIdObj = _connectionService.ExecuteScalar(getTestingStepIdSql);

            if (testingStepIdObj == null || testingStepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step 'Testing' was not found or is inactive.");
            }

            int testingStepId = Convert.ToInt32(testingStepIdObj);

            const string insertNextStepSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            int InsertApproval(int stepId, int assignedTo)
            {
                var p = new DynamicParameters();
                p.Add("@CRID", crId);
                p.Add("@StepID", stepId);
                p.Add("@AssignedBy", approvedByEmpId);
                p.Add("@AssignedTo", assignedTo);
                p.Add("@AssignedDate", DateTime.Now);
                p.Add("@Active", true);

                return _connectionService.ExecuteWithPara(insertNextStepSql, p);
            }

            int assessmentRows = InsertApproval(assessmentStepId, approverId);
            int testingRows = InsertApproval(testingStepId, testerId);

            return assessmentRows > 0 && testingRows > 0;
        }

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsDraftsAsync(string empNo)
        {
            //// 1. Fetch Rejected StatusID dynamically from CRStatus
            //const string getRejectedStatusSql = @"
            //    SELECT TOP 1 StatusID 
            //    FROM [CRManagementDB].[dbo].[CRStatus] 
            //    WHERE StatusName LIKE '%Reject%' AND Active = 1";

            //var rejectedStatusObj = _connectionService.ExecuteScalar(getRejectedStatusSql);

            //if (rejectedStatusObj == null || rejectedStatusObj == DBNull.Value)
            //{
            //    throw new InvalidOperationException("Status 'Rejected' was not found or is inactive in CRStatus table.");
            //}

            //int rejectedStatusId = Convert.ToInt32(rejectedStatusObj);

            const string sql = @"
                SELECT
                    cr.CRID,
                    cr.CRNumber,
                    cr.ChangeTitle,
                    cr.Summary,
                    ct.ChangeTypeName AS ChangeType,
                    p.PriorityName AS Priority,
                    cr.DivisionID,
                    d.DivisionName AS Division,
                    cr.ModuleID,
                    m.ModuleName AS Module,
                    s.StatusName AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo AS ApproverUserName,
                    cr.RequestedDate
                FROM [CRManagementDB].[dbo].[ChangeRequest] AS cr
                OUTER APPLY (
                    SELECT TOP 1 a.AssignedTo
                    FROM [CRManagementDB].[dbo].[Approval] AS a
                    WHERE a.CRID = cr.CRID
                      AND a.Active = 1
                    ORDER BY a.ApprovalID DESC
                ) AS App
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct
                    ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p
                    ON p.PriorityID = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division] AS d
                    ON d.DivisionID = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module] AS m
                    ON m.ModuleID = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s
                    ON s.StatusID = cr.StatusID
                WHERE cr.Active = 1
                    AND cr.RequesterUserName = @EmpNo
                ORDER BY
                    cr.CRID DESC;";
            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo
                }).ToList();

            if (!changeRequests.Any())
                return changeRequests;

            // 3. Resolve Full Names and prepend UserName/EmpNo from Users Table
            var userNames = changeRequests
                .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
                    SELECT UserName, FirstName, LastName
                    FROM Users
                    WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .ToDictionary(
                        r => r.Field<string>("UserName"),
                        r =>
                        {
                            var uName = r.Field<string>("UserName");
                            var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
                            return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
                        },
                        StringComparer.OrdinalIgnoreCase);

                foreach (var cr in changeRequests)
                {
                    if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
                        nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
                    {
                        cr.RequestedBy = requesterFormattedName;
                    }

                    if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
                        nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
                    {
                        cr.ApproverUserName = approverFormattedName;
                    }
                }
            }

            return changeRequests;
        }

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAssignedByMeAsync(string empNo)
        {
            const string sql = @"
                SELECT
                    x.CRID,
                    x.CRNumber,
                    x.ChangeTitle,
                    x.Summary,
                    x.ChangeType,
                    x.Priority,
                    x.DivisionID,
                    x.Division,
                    x.ModuleID,
                    x.Module,
                    x.Status,
                    x.RequestedBy,
                    x.ApproverUserName,
                    x.RequestedDate
                FROM (
                    SELECT
                        cr.CRID,
                        cr.CRNumber,
                        cr.ChangeTitle,
                        cr.Summary,
                        ct.ChangeTypeName AS ChangeType,
                        p.PriorityName    AS Priority,
                        cr.DivisionID,
                        d.DivisionName    AS Division,
                        cr.ModuleID,
                        m.ModuleName      AS Module,
                        s.StatusName      AS Status,
                        cr.RequesterUserName AS RequestedBy,
                        App.AssignedTo    AS ApproverUserName,
                        cr.RequestedDate,
                        ROW_NUMBER() OVER (PARTITION BY cr.CRID ORDER BY App.ApprovalID DESC) AS rn
                    FROM [CRManagementDB].[dbo].[Approval] AS App
                    INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                    LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                    LEFT JOIN [CRManagementDB].[dbo].[Priority]   AS p  ON p.PriorityID   = cr.PriorityID
                    LEFT JOIN [CRManagementDB].[dbo].[Division]   AS d  ON d.DivisionID   = cr.DivisionID
                    LEFT JOIN [CRManagementDB].[dbo].[Module]     AS m  ON m.ModuleID     = cr.ModuleID
                    LEFT JOIN [CRManagementDB].[dbo].[CRStatus]   AS s  ON s.StatusID     = cr.StatusID
                    WHERE App.AssignedBy = @EmpNo
                      AND App.Active = 1
                      AND cr.Active = 1
                ) AS x
                WHERE x.rn = 1
                ORDER BY x.CRNumber DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new { EmpNo = empNo }).ToList();

            if (!changeRequests.Any())
                return changeRequests;

            var userNames = changeRequests
                .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
            SELECT UserName, FirstName, LastName
            FROM Users
            WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .ToDictionary(
                        r => r.Field<string>("UserName"),
                        r =>
                        {
                            var uName = r.Field<string>("UserName");
                            var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
                            return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
                        },
                        StringComparer.OrdinalIgnoreCase);

                foreach (var cr in changeRequests)
                {
                    if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
                        nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
                    {
                        cr.RequestedBy = requesterFormattedName;
                    }

                    if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
                        nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
                    {
                        cr.ApproverUserName = approverFormattedName;
                    }
                }
            }

            return changeRequests;
        }

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAssignedToMeAsync(string empNo)
        {
            const string sql = @"
                SELECT
                    x.CRID,
                    x.CRNumber,
                    x.ChangeTitle,
                    x.Summary,
                    x.ChangeType,
                    x.Priority,
                    x.DivisionID,
                    x.Division,
                    x.ModuleID,
                    x.Module,
                    x.Status,
                    x.RequestedBy,
                    x.ApproverUserName,
                    x.RequestedDate
                FROM (
                    SELECT
                        cr.CRID,
                        cr.CRNumber,
                        cr.ChangeTitle,
                        cr.Summary,
                        ct.ChangeTypeName AS ChangeType,
                        p.PriorityName    AS Priority,
                        cr.DivisionID,
                        d.DivisionName    AS Division,
                        cr.ModuleID,
                        m.ModuleName      AS Module,
                        s.StatusName      AS Status,
                        cr.RequesterUserName AS RequestedBy,
                        App.AssignedTo    AS ApproverUserName,
                        cr.RequestedDate,
                        ROW_NUMBER() OVER (PARTITION BY cr.CRID ORDER BY App.ApprovalID DESC) AS rn
                    FROM [CRManagementDB].[dbo].[Approval] AS App
                    INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                    LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                    LEFT JOIN [CRManagementDB].[dbo].[Priority]   AS p  ON p.PriorityID   = cr.PriorityID
                    LEFT JOIN [CRManagementDB].[dbo].[Division]   AS d  ON d.DivisionID   = cr.DivisionID
                    LEFT JOIN [CRManagementDB].[dbo].[Module]     AS m  ON m.ModuleID     = cr.ModuleID
                    LEFT JOIN [CRManagementDB].[dbo].[CRStatus]   AS s  ON s.StatusID     = cr.StatusID
                    WHERE App.AssignedTo = @EmpNo
                      AND App.Active = 1
                      AND cr.Active = 1
                ) AS x
                WHERE x.rn = 1
                ORDER BY x.CRNumber DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new { EmpNo = empNo }).ToList();

            if (!changeRequests.Any())
                return changeRequests;

            var userNames = changeRequests
                .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
            SELECT UserName, FirstName, LastName
            FROM Users
            WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .ToDictionary(
                        r => r.Field<string>("UserName"),
                        r =>
                        {
                            var uName = r.Field<string>("UserName");
                            var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
                            return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
                        },
                        StringComparer.OrdinalIgnoreCase);

                foreach (var cr in changeRequests)
                {
                    if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
                        nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
                    {
                        cr.RequestedBy = requesterFormattedName;
                    }

                    if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
                        nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
                    {
                        cr.ApproverUserName = approverFormattedName;
                    }
                }
            }

            return changeRequests;
        }

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsSubmissionsAsync(string empNo)
        {
            // 1. Division Head Approval StepID
            const string getStepSql = @"
                SELECT TOP 1 StepID 
                FROM [CRManagementDB].[dbo].[WorkflowStep] 
                WHERE StepName LIKE '%Division Head Approval%' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getStepSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step 'Division Head Approval' was not found or is inactive.");
            }

            int departmentHeadStepId = Convert.ToInt32(stepIdObj);

            // 2. Fetch all non-Draft StatusIDs
            const string getStatusIdsSql = @"
                SELECT StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE StatusName != 'Draft'
                AND Active = 1";

            var statusTable = _connectionService.ReturnWithPara(getStatusIdsSql, null);
            var statusIds = statusTable.AsEnumerable()
                .Select(r => r.Field<int>("StatusID"))
                .ToList();

            if (!statusIds.Any())
            {
                return Enumerable.Empty<ChangeRequest>();
            }

            const string sql = @"
                    SELECT
                    cr.CRID,
                    cr.CRNumber,
                    cr.ChangeTitle,
                    cr.Summary,
                    ct.ChangeTypeName AS ChangeType,
                    p.PriorityName AS Priority,
                    cr.DivisionID,
                    d.DivisionName AS Division,
                    cr.ModuleID,
                    m.ModuleName AS Module,
                    s.StatusName AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo AS ApproverUserName,
                    cr.RequestedDate
                FROM [dbo].[Approval] AS App
                INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
                WHERE cr.Active = 1
                    AND App.Active = 1
                    AND App.StepID = @StepID
                    AND App.AssignedTo = @EmpNo
                    AND cr.StatusID IN @StatusIDs
                ORDER BY
                    cr.CRNumber DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo,
                    StepID = departmentHeadStepId,
                    StatusIDs = statusIds
                }).ToList();

            if (!changeRequests.Any())
                return changeRequests;

            // 4. Resolve Full Names for Requesters and Approvers
            var userNames = changeRequests
                .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
                    SELECT UserName, FirstName, LastName
                    FROM Users
                    WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .ToDictionary(
                        r => r.Field<string>("UserName"),
                        r =>
                        {
                            var uName = r.Field<string>("UserName");
                            var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
                            return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
                        },
                        StringComparer.OrdinalIgnoreCase);

                foreach (var cr in changeRequests)
                {
                    if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
                        nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
                    {
                        cr.RequestedBy = requesterFormattedName;
                    }

                    if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
                        nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
                    {
                        cr.ApproverUserName = approverFormattedName;
                    }
                }
            }

            return changeRequests;
        }

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsRejectionsAsync(string empNo)
        {
            // 1. Fetch the Rejected StatusID dynamically from CRStatus table
            const string getRejectedStatusIdSql = @"
                SELECT TOP 1 StatusID
                FROM [CRManagementDB].[dbo].[CRStatus]
                WHERE StatusName LIKE '%Reject%' AND Active = 1";

            var statusIdObj = _connectionService.ExecuteScalar(getRejectedStatusIdSql);

            if (statusIdObj == null || statusIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Status 'Rejected' was not found or is inactive in CRStatus table.");
            }

            int rejectedStatusId = Convert.ToInt32(statusIdObj);

            // 2. Fetch Change Requests matching the Rejected Status and Approval conditions
            const string sql = @"
                SELECT
                    cr.CRID,
                    cr.CRNumber,
                    cr.ChangeTitle,
                    cr.Summary,
                    ct.ChangeTypeName AS ChangeType,
                    p.PriorityName AS Priority,
                    cr.DivisionID,
                    d.DivisionName AS Division,
                    cr.ModuleID,
                    m.ModuleName AS Module,
                    s.StatusName AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo AS ApproverUserName,
                    cr.RequestedDate
                FROM [dbo].[Approval] AS App
                INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
                WHERE cr.Active = 1
                    AND cr.StatusID = @StatusID
                    AND (App.IsApproved = 0)
                    AND (App.AssignedTo = @EmpNo OR App.AssignedBy = @EmpNo OR cr.RequesterUserName = @EmpNo)
                ORDER BY
                    cr.CRID DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo,
                    StatusID = rejectedStatusId
                }).ToList();

            if (!changeRequests.Any())
                return changeRequests;

            // 3. Resolve Full Names for Requesters and Approvers
            var userNames = changeRequests
                .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
                    SELECT UserName, FirstName, LastName
                    FROM Users
                    WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .ToDictionary(
                        r => r.Field<string>("UserName"),
                        r =>
                        {
                            var uName = r.Field<string>("UserName");
                            var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
                            return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
                        },
                        StringComparer.OrdinalIgnoreCase);

                foreach (var cr in changeRequests)
                {
                    if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
                        nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
                    {
                        cr.RequestedBy = requesterFormattedName;
                    }

                    if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
                        nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
                    {
                        cr.ApproverUserName = approverFormattedName;
                    }
                }
            }

            return changeRequests;
        }
        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingAssignAsync(string empNo)
        {
            const string getDeptHeadStepSql = @"
                SELECT TOP 1 StepID 
                FROM [CRManagementDB].[dbo].[WorkflowStep] 
                WHERE StepName = 'Division Head Approval' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getDeptHeadStepSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step 'Division Head Approval' was not found or is inactive.");
            }

            int deptHeadStepId = Convert.ToInt32(stepIdObj);

            // Get all StatusIDs NOT in the excluded set (Draft, Submitted, Approved, Assessment)
            const string getTestingStatusIdsSql = @"
                SELECT StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE StatusName != 'Draft'
                  AND StatusName != 'Submitted'
                  AND StatusName != 'Approved'
                  AND StatusName != 'Assessment'
                  AND Active = 1";

            var statusTable = _connectionService.ReturnWithPara(getTestingStatusIdsSql, null);
            var statusIds = statusTable.AsEnumerable()
                .Select(r => r.Field<int>("StatusID"))
                .ToList();

            if (!statusIds.Any())
            {
                return Enumerable.Empty<ChangeRequest>();
            }

            const string sql = @"
                SELECT
                    cr.CRID,
                    cr.CRNumber,
                    cr.ChangeTitle,
                    cr.Summary,
                    cr.ProposalNumber,
                    ct.ChangeTypeName AS ChangeType,
                    p.PriorityName AS Priority,
                    cr.DivisionID,
                    d.DivisionName AS Division,
                    cr.ModuleID,
                    cr.ActivitiesTasks,
                    cr.FixedAssets,
                    cr.RiskAssessment,
                    m.ModuleName AS Module,
                    s.StatusName AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo AS ApproverUserName,
                    cr.RequestedDate,
                    v.VendorName AS Vendor,
                    ci.ImpactName AS ChangeImpact
                FROM [dbo].[Approval] AS App
                INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
                LEFT JOIN [CRManagementDB].[dbo].[Vendor] AS v ON v.VendorID = cr.VendorID 
                LEFT JOIN [CRManagementDB].[dbo].[ChangeImpact] AS ci ON ci.ImpactID = cr.ImpactID
                WHERE cr.Active = 1
                    AND App.Active = 1
                    AND App.IsApproved = 1
                    AND App.StepID = @StepID
                    AND App.AssignedTo = @EmpNo
                    AND cr.StatusID IN @StatusIDs
                ORDER BY
                    cr.CRID DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo,
                    StepID = deptHeadStepId,
                    StatusIDs = statusIds
                }).ToList();

            if (!changeRequests.Any())
                return changeRequests;

            // 4. Resolve Full Names for Requesters and Approvers
            var userNames = changeRequests
                .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
                    SELECT UserName, FirstName, LastName
                    FROM Users
                    WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .ToDictionary(
                        r => r.Field<string>("UserName"),
                        r =>
                        {
                            var uName = r.Field<string>("UserName");
                            var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
                            return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
                        },
                        StringComparer.OrdinalIgnoreCase);

                foreach (var cr in changeRequests)
                {
                    if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
                        nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
                    {
                        cr.RequestedBy = requesterFormattedName;
                    }

                    if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
                        nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
                    {
                        cr.ApproverUserName = approverFormattedName;
                    }
                }
            }

            var crIds = changeRequests.Select(cr => cr.CRID).Distinct().ToList();

            const string attachmentsQuery = @"
                SELECT AttachmentID, CRID, FileName, FilePath, UploadedBy, UploadedDate, Active
                FROM [CRManagementDB].[dbo].[Attachment]
                WHERE CRID IN @CRIDs AND Active = 1
                ORDER BY UploadedDate DESC";

            var attachmentParams = new DynamicParameters();
            attachmentParams.Add("@CRIDs", crIds);

            var attachmentsTable = _connectionService.ReturnWithPara(attachmentsQuery, attachmentParams);

            var attachmentsByCrId = attachmentsTable.AsEnumerable()
                .GroupBy(r => r.Field<int>("CRID"))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => new Attachment
                    {
                        AttachmentID = r.Field<int>("AttachmentID"),
                        CRID = r.Field<int>("CRID"),
                        FileName = r.Field<string>("FileName"),
                        FilePath = r.Field<string>("FilePath"),
                        UploadedBy = r.Field<string>("UploadedBy"),
                        UploadedDate = r.Field<DateTime>("UploadedDate"),
                        Active = r.Field<bool>("Active")
                    }).ToList()
                );

            foreach (var cr in changeRequests)
            {
                cr.Attachments = attachmentsByCrId.TryGetValue(cr.CRID, out var files)
                    ? files
                    : new List<Attachment>();
            }

            return changeRequests;
        }
        //public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingQueueAsync(string empNo)
        //{
        //    // 1. Fetch Assessment StepID dynamically from WorkflowStep
        //    const string getAssessmentStepSql = @"
        //        SELECT TOP 1 StepID 
        //        FROM [CRManagementDB].[dbo].[WorkflowStep] 
        //        WHERE StepName LIKE '%Testing%' AND Active = 1
        //        ORDER BY StepOrder ASC";

        //    var stepIdObj = _connectionService.ExecuteScalar(getAssessmentStepSql);

        //    if (stepIdObj == null || stepIdObj == DBNull.Value)
        //    {
        //        throw new InvalidOperationException("Workflow step for 'Security' was not found or is inactive.");
        //    }

        //    int assessmentStepId = Convert.ToInt32(stepIdObj);

        //    const string getStatusIdsSql = @"
        //        SELECT StatusID 
        //        FROM [CRManagementDB].[dbo].[CRStatus] 
        //        WHERE StatusName != 'Draft'
        //          AND StatusName != 'Submitted'
        //          AND StatusName != 'Approved'
        //          AND StatusName != 'Assessment'
        //          AND StatusName != 'Security'
        //          AND Active = 1";

        //    var statusTable = _connectionService.ReturnWithPara(getStatusIdsSql, null);
        //    var statusIds = statusTable.AsEnumerable()
        //        .Select(r => r.Field<int>("StatusID"))
        //        .ToList();

        //    if (!statusIds.Any())
        //    {
        //        return Enumerable.Empty<ChangeRequest>();
        //    }

        //    // 3. Query Change Requests joined with Approval
        //    const string sql = @"
        //        SELECT
        //            cr.CRID,
        //            cr.CRNumber,
        //            cr.ChangeTitle,
        //            cr.Summary,
        //            cr.ProposalNumber,
        //            ct.ChangeTypeName AS ChangeType,
        //            p.PriorityName AS Priority,
        //            cr.DivisionID,
        //            d.DivisionName AS Division,
        //            cr.ModuleID,
        //            cr.ActivitiesTasks,
        //            cr.FixedAssets,
        //            cr.RiskAssessment,
        //            m.ModuleName AS Module,
        //            s.StatusName AS Status,
        //            cr.RequesterUserName AS RequestedBy,
        //            App.AssignedTo AS ApproverUserName,
        //            cr.RequestedDate,
        //            v.VendorName AS Vendor,
        //            ci.ImpactName AS ChangeImpact
        //        FROM [dbo].[Approval] AS App
        //        INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
        //        LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
        //        LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
        //        LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
        //        LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
        //        LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
        //        LEFT JOIN [CRManagementDB].[dbo].[Vendor] AS v ON v.VendorID = cr.VendorID 
        //        LEFT JOIN [CRManagementDB].[dbo].[ChangeImpact] AS ci ON ci.ImpactID = cr.ImpactID
        //        WHERE cr.Active = 1
        //            AND App.Active = 1
        //            AND App.StepID = @StepID
        //            AND App.AssignedTo = @EmpNo
        //            AND cr.StatusID IN @StatusIDs
        //        ORDER BY
        //            cr.CRID DESC;";

        //    var changeRequests = _connectionService.Query<ChangeRequest>(
        //        sql,
        //        new
        //        {
        //            EmpNo = empNo,
        //            StepID = assessmentStepId,
        //            StatusIDs = statusIds
        //        }).ToList();

        //    if (!changeRequests.Any())
        //        return changeRequests;

        //    // 4. Resolve Full Names for Requesters and Approvers
        //    var userNames = changeRequests
        //        .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
        //        .Where(u => !string.IsNullOrWhiteSpace(u))
        //        .Distinct()
        //        .ToList();

        //    if (userNames.Any())
        //    {
        //        const string usersQuery = @"
        //            SELECT UserName, FirstName, LastName
        //            FROM Users
        //            WHERE UserName IN @UserNames";

        //        var userParams = new DynamicParameters();
        //        userParams.Add("@UserNames", userNames);

        //        var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

        //        var nameLookup = usersTable.AsEnumerable()
        //            .ToDictionary(
        //                r => r.Field<string>("UserName"),
        //                r =>
        //                {
        //                    var uName = r.Field<string>("UserName");
        //                    var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
        //                    return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
        //                },
        //                StringComparer.OrdinalIgnoreCase);

        //        foreach (var cr in changeRequests)
        //        {
        //            if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
        //                nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
        //            {
        //                cr.RequestedBy = requesterFormattedName;
        //            }

        //            if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
        //                nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
        //            {
        //                cr.ApproverUserName = approverFormattedName;
        //            }
        //        }
        //    }

        //    // 5. Fetch attachments for all CRs in a single round-trip
        //    var crIds = changeRequests.Select(cr => cr.CRID).Distinct().ToList();

        //    const string attachmentsQuery = @"
        //        SELECT AttachmentID, CRID, FileName, FilePath, UploadedBy, UploadedDate, Active
        //        FROM [CRManagementDB].[dbo].[Attachment]
        //        WHERE CRID IN @CRIDs AND Active = 1
        //        ORDER BY UploadedDate DESC";

        //    var attachmentParams = new DynamicParameters();
        //    attachmentParams.Add("@CRIDs", crIds);

        //    var attachmentsTable = _connectionService.ReturnWithPara(attachmentsQuery, attachmentParams);

        //    var attachmentsByCrId = attachmentsTable.AsEnumerable()
        //        .GroupBy(r => r.Field<int>("CRID"))
        //        .ToDictionary(
        //            g => g.Key,
        //            g => g.Select(r => new Attachment
        //            {
        //                AttachmentID = r.Field<int>("AttachmentID"),
        //                CRID = r.Field<int>("CRID"),
        //                FileName = r.Field<string>("FileName"),
        //                FilePath = r.Field<string>("FilePath"),
        //                UploadedBy = r.Field<string>("UploadedBy"),
        //                UploadedDate = r.Field<DateTime>("UploadedDate"),
        //                Active = r.Field<bool>("Active")
        //            }).ToList()
        //        );

        //    foreach (var cr in changeRequests)
        //    {
        //        cr.Attachments = attachmentsByCrId.TryGetValue(cr.CRID, out var files)
        //            ? files
        //            : new List<Attachment>();
        //    }

        //    return changeRequests;
        //}


        //public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingApprovalsAsync(string empNo)
        //{
        //    const string getDeptHeadStepSql = @"
        //        SELECT TOP 1 StepID 
        //        FROM [CRManagementDB].[dbo].[WorkflowStep] 
        //        WHERE StepName = 'Division Head Approval' AND Active = 1
        //        ORDER BY StepOrder ASC";

        //    var stepIdObj = _connectionService.ExecuteScalar(getDeptHeadStepSql);

        //    if (stepIdObj == null || stepIdObj == DBNull.Value)
        //    {
        //        throw new InvalidOperationException("Workflow step 'Division Head Approval' was not found or is inactive.");
        //    }

        //    int deptHeadStepId = Convert.ToInt32(stepIdObj);

        //    // Get all StatusIDs NOT in the excluded set (Draft, Submitted, Approved, Assessment)
        //    const string getTestingStatusIdsSql = @"
        //        SELECT StatusID 
        //        FROM [CRManagementDB].[dbo].[CRStatus] 
        //        WHERE StatusName != 'Draft'
        //          AND StatusName != 'Submitted'
        //          AND StatusName != 'Approved'
        //          AND StatusName != 'Assessment'
        //          AND StatusName != 'Security'
        //          AND Active = 1";

        //    var statusTable = _connectionService.ReturnWithPara(getTestingStatusIdsSql, null);
        //    var statusIds = statusTable.AsEnumerable()
        //        .Select(r => r.Field<int>("StatusID"))
        //        .ToList();

        //    if (!statusIds.Any())
        //    {
        //        return Enumerable.Empty<ChangeRequest>();
        //    }

        //    const string sql = @"
        //        SELECT
        //            cr.CRID,
        //            cr.CRNumber,
        //            cr.ChangeTitle,
        //            cr.Summary,
        //            cr.ProposalNumber,
        //            ct.ChangeTypeName AS ChangeType,
        //            p.PriorityName AS Priority,
        //            cr.DivisionID,
        //            d.DivisionName AS Division,
        //            cr.ModuleID,
        //            cr.ActivitiesTasks,
        //            cr.FixedAssets,
        //            cr.RiskAssessment,
        //            m.ModuleName AS Module,
        //            s.StatusName AS Status,
        //            cr.RequesterUserName AS RequestedBy,
        //            App.AssignedTo AS ApproverUserName,
        //            cr.RequestedDate,
        //            v.VendorName AS Vendor,
        //            ci.ImpactName AS ChangeImpact
        //        FROM [dbo].[Approval] AS App
        //        INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
        //        LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
        //        LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
        //        LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
        //        LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
        //        LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
        //        LEFT JOIN [CRManagementDB].[dbo].[Vendor] AS v ON v.VendorID = cr.VendorID 
        //        LEFT JOIN [CRManagementDB].[dbo].[ChangeImpact] AS ci ON ci.ImpactID = cr.ImpactID
        //        WHERE cr.Active = 1
        //            AND App.Active = 1
        //            AND App.IsApproved = 1
        //            AND App.StepID = @StepID
        //            AND App.AssignedTo = @EmpNo
        //            AND cr.StatusID IN @StatusIDs
        //        ORDER BY
        //            cr.CRID DESC;";

        //    var changeRequests = _connectionService.Query<ChangeRequest>(
        //        sql,
        //        new
        //        {
        //            EmpNo = empNo,
        //            StepID = deptHeadStepId,
        //            StatusIDs = statusIds
        //        }).ToList();

        //    if (!changeRequests.Any())
        //        return changeRequests;

        //    // 4. Resolve Full Names for Requesters and Approvers
        //    var userNames = changeRequests
        //        .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
        //        .Where(u => !string.IsNullOrWhiteSpace(u))
        //        .Distinct()
        //        .ToList();

        //    if (userNames.Any())
        //    {
        //        const string usersQuery = @"
        //            SELECT UserName, FirstName, LastName
        //            FROM Users
        //            WHERE UserName IN @UserNames";

        //        var userParams = new DynamicParameters();
        //        userParams.Add("@UserNames", userNames);

        //        var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

        //        var nameLookup = usersTable.AsEnumerable()
        //            .ToDictionary(
        //                r => r.Field<string>("UserName"),
        //                r =>
        //                {
        //                    var uName = r.Field<string>("UserName");
        //                    var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
        //                    return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
        //                },
        //                StringComparer.OrdinalIgnoreCase);

        //        foreach (var cr in changeRequests)
        //        {
        //            if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
        //                nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
        //            {
        //                cr.RequestedBy = requesterFormattedName;
        //            }

        //            if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
        //                nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
        //            {
        //                cr.ApproverUserName = approverFormattedName;
        //            }
        //        }
        //    }

        //    var crIds = changeRequests.Select(cr => cr.CRID).Distinct().ToList();

        //    const string attachmentsQuery = @"
        //        SELECT AttachmentID, CRID, FileName, FilePath, UploadedBy, UploadedDate, Active
        //        FROM [CRManagementDB].[dbo].[Attachment]
        //        WHERE CRID IN @CRIDs AND Active = 1
        //        ORDER BY UploadedDate DESC";

        //    var attachmentParams = new DynamicParameters();
        //    attachmentParams.Add("@CRIDs", crIds);

        //    var attachmentsTable = _connectionService.ReturnWithPara(attachmentsQuery, attachmentParams);

        //    var attachmentsByCrId = attachmentsTable.AsEnumerable()
        //        .GroupBy(r => r.Field<int>("CRID"))
        //        .ToDictionary(
        //            g => g.Key,
        //            g => g.Select(r => new Attachment
        //            {
        //                AttachmentID = r.Field<int>("AttachmentID"),
        //                CRID = r.Field<int>("CRID"),
        //                FileName = r.Field<string>("FileName"),
        //                FilePath = r.Field<string>("FilePath"),
        //                UploadedBy = r.Field<string>("UploadedBy"),
        //                UploadedDate = r.Field<DateTime>("UploadedDate"),
        //                Active = r.Field<bool>("Active")
        //            }).ToList()
        //        );

        //    foreach (var cr in changeRequests)
        //    {
        //        cr.Attachments = attachmentsByCrId.TryGetValue(cr.CRID, out var files)
        //            ? files
        //            : new List<Attachment>();
        //    }

        //    return changeRequests;
        //}

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsFinalApprovalsAsync(string empNo)
        {
            const string getDeptHeadStepSql = @"
                SELECT TOP 1 StepID 
                FROM [CRManagementDB].[dbo].[WorkflowStep] 
                WHERE StepName = 'Final Approval' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getDeptHeadStepSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step 'Final Approval' was not found or is inactive.");
            }

            int deptHeadStepId = Convert.ToInt32(stepIdObj);

            // Get all StatusIDs NOT in the excluded set (Draft, Submitted, Approved, Assessment)
            const string getTestingStatusIdsSql = @"
                SELECT StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE StatusName = 'TestingApproved'
                  AND Active = 1";

            var statusTable = _connectionService.ReturnWithPara(getTestingStatusIdsSql, null);
            var statusIds = statusTable.AsEnumerable()
                .Select(r => r.Field<int>("StatusID"))
                .ToList();

            if (!statusIds.Any())
            {
                return Enumerable.Empty<ChangeRequest>();
            }

            const string sql = @"
                SELECT
                    cr.CRID,
                    cr.CRNumber,
                    cr.ChangeTitle,
                    cr.Summary,
                    cr.ProposalNumber,
                    ct.ChangeTypeName AS ChangeType,
                    p.PriorityName AS Priority,
                    cr.DivisionID,
                    d.DivisionName AS Division,
                    cr.ModuleID,
                    cr.ActivitiesTasks,
                    cr.FixedAssets,
                    cr.RiskAssessment,
                    m.ModuleName AS Module,
                    s.StatusName AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo AS ApproverUserName,
                    cr.RequestedDate,
                    v.VendorName AS Vendor,
                    ci.ImpactName AS ChangeImpact
                FROM [dbo].[Approval] AS App
                INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
                LEFT JOIN [CRManagementDB].[dbo].[Vendor] AS v ON v.VendorID = cr.VendorID 
                LEFT JOIN [CRManagementDB].[dbo].[ChangeImpact] AS ci ON ci.ImpactID = cr.ImpactID
                WHERE cr.Active = 1
                    AND App.Active = 1
                    AND App.StepID = @StepID
                    AND App.AssignedTo = @EmpNo
                    AND cr.StatusID IN @StatusIDs
                ORDER BY
                    cr.CRID DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo,
                    StepID = deptHeadStepId,
                    StatusIDs = statusIds
                }).ToList();

            if (!changeRequests.Any())
                return changeRequests;

            // 4. Resolve Full Names for Requesters and Approvers
            var userNames = changeRequests
                .SelectMany(cr => new[] { cr.RequestedBy, cr.ApproverUserName })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (userNames.Any())
            {
                const string usersQuery = @"
                    SELECT UserName, FirstName, LastName
                    FROM Users
                    WHERE UserName IN @UserNames";

                var userParams = new DynamicParameters();
                userParams.Add("@UserNames", userNames);

                var usersTable = _connectionService.ReturnWithPara2(usersQuery, userParams);

                var nameLookup = usersTable.AsEnumerable()
                    .ToDictionary(
                        r => r.Field<string>("UserName"),
                        r =>
                        {
                            var uName = r.Field<string>("UserName");
                            var fullName = $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim();
                            return string.IsNullOrWhiteSpace(fullName) ? uName : $"{uName} - {fullName}";
                        },
                        StringComparer.OrdinalIgnoreCase);

                foreach (var cr in changeRequests)
                {
                    if (!string.IsNullOrWhiteSpace(cr.RequestedBy) &&
                        nameLookup.TryGetValue(cr.RequestedBy, out var requesterFormattedName))
                    {
                        cr.RequestedBy = requesterFormattedName;
                    }

                    if (!string.IsNullOrWhiteSpace(cr.ApproverUserName) &&
                        nameLookup.TryGetValue(cr.ApproverUserName, out var approverFormattedName))
                    {
                        cr.ApproverUserName = approverFormattedName;
                    }
                }
            }

            var crIds = changeRequests.Select(cr => cr.CRID).Distinct().ToList();

            const string attachmentsQuery = @"
                SELECT AttachmentID, CRID, FileName, FilePath, UploadedBy, UploadedDate, Active
                FROM [CRManagementDB].[dbo].[Attachment]
                WHERE CRID IN @CRIDs AND Active = 1
                ORDER BY UploadedDate DESC";

            var attachmentParams = new DynamicParameters();
            attachmentParams.Add("@CRIDs", crIds);

            var attachmentsTable = _connectionService.ReturnWithPara(attachmentsQuery, attachmentParams);

            var attachmentsByCrId = attachmentsTable.AsEnumerable()
                .GroupBy(r => r.Field<int>("CRID"))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => new Attachment
                    {
                        AttachmentID = r.Field<int>("AttachmentID"),
                        CRID = r.Field<int>("CRID"),
                        FileName = r.Field<string>("FileName"),
                        FilePath = r.Field<string>("FilePath"),
                        UploadedBy = r.Field<string>("UploadedBy"),
                        UploadedDate = r.Field<DateTime>("UploadedDate"),
                        Active = r.Field<bool>("Active")
                    }).ToList()
                );

            foreach (var cr in changeRequests)
            {
                cr.Attachments = attachmentsByCrId.TryGetValue(cr.CRID, out var files)
                    ? files
                    : new List<Attachment>();
            }

            return changeRequests;
        }




        public async Task<bool> RejectChangeRequestAsync(
            int crId,
            string rejectReason,
            string rejectedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            if (string.IsNullOrWhiteSpace(rejectReason))
                throw new ArgumentException("Please provide a reason for rejection.");

            const string updateApprovalSql = @"
                UPDATE a
                SET
                    a.ApprovalDate = @ApprovalDate,
                    a.IsApproved = @IsApproved,
                    a.Comments = @Comments
                FROM Approval AS a
                WHERE a.CRID = @CRID
                  AND a.Active = 1";

            var approvalParameters = new DynamicParameters();
            approvalParameters.Add("@ApprovalDate", DateTime.Now);
            approvalParameters.Add("@IsApproved", false);
            approvalParameters.Add("@Comments", rejectReason);
            approvalParameters.Add("@CRID", crId);

            int approvalRowsAffected =
                _connectionService.ExecuteWithPara(
                    updateApprovalSql,
                    approvalParameters);

            if (approvalRowsAffected <= 0)
                return false;

            // 3. Move the ChangeRequest itself into "Rejected" status
            const string updateStatusSql = @"
                UPDATE cr
                SET cr.StatusID = s.StatusID
                FROM ChangeRequest AS cr
                INNER JOIN CRStatus AS s
                    ON s.StatusName = @StatusName
                    AND s.Active = 1
                WHERE cr.CRID = @CRID";

            var statusParameters = new DynamicParameters();
            statusParameters.Add("@StatusName", "Rejected");
            statusParameters.Add("@CRID", crId);

            int statusRowsAffected =
                _connectionService.ExecuteWithPara(
                    updateStatusSql,
                    statusParameters);

            return statusRowsAffected > 0;
        }

        public async Task<bool> SubmitChangeRequestAsync(int crId, string submittedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            const int submittedStatusId = 2;
            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var newCrNumber = GenerateNextCrNumber();

                const string updateSql = @"
                UPDATE ChangeRequest
                SET CRNumber = @CRNumber,
                    StatusID = @StatusID
                WHERE CRID = @CRID
                  AND CRNumber LIKE 'Waiting-%'";

                var parameters = new DynamicParameters();
                parameters.Add("@CRNumber", newCrNumber);
                parameters.Add("@StatusID", submittedStatusId);
                parameters.Add("@CRID", crId);

                try
                {
                    int rowsAffected = _connectionService.ExecuteWithPara(updateSql, parameters);
                    return rowsAffected > 0;
                }
                catch (Exception ex) when (attempt < maxAttempts && IsDuplicateCrNumberError(ex))
                {
                    continue;
                }
            }

            return false;
        }

        public async Task<bool> DeleteChangeRequestAsync(int crId, string requestedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            const string deleteApprovalsSql = @"
            DELETE FROM Approval
            WHERE CRID = @CRID";

            const string deleteCrSql = @"
            DELETE FROM ChangeRequest
            WHERE CRID = @CRID
              AND CRNumber LIKE 'Waiting-%'";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);

            _connectionService.ExecuteWithPara(deleteApprovalsSql, parameters);
            int rowsAffected = _connectionService.ExecuteWithPara(deleteCrSql, parameters);

            return rowsAffected > 0;
        }

        public async Task<bool> CreateAssessmentAsync(int crId, IFormCollection collection, string userName, string empId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            var activitiesTasks = collection["ActivitiesTasks"].ToString();
            if (string.IsNullOrWhiteSpace(activitiesTasks))
            {
                throw new ArgumentException("Please fill out Activities & Tasks.");
            }

            var fixedAssetInfo = collection["FixedAssetInfo"].ToString();
            if (string.IsNullOrWhiteSpace(fixedAssetInfo))
            {
                throw new ArgumentException("Please fill out Fixed Asset Info.");
            }

            var estimationDaysStr = collection["EffortEstimateDays"].ToString();

            double.TryParse(estimationDaysStr, out double estimationDays);
            DateTime targetDate = DateTime.Now.AddDays(estimationDays);

            var vendorID = collection["VendorID"].ToString();
            var proposalNumber = collection["ProposalNumber"].ToString();

            // "1" = Save as Draft
            // "2" = Submit
            var statusValue = collection["Status"].ToString();
            bool isSubmit = statusValue == "2";

            var targetStatusName = isSubmit
                ? "Assessment"
                : "AssessmentDraft";

            const string updateCrSql = @"
                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET 
                    ActivitiesTasks = @ActivitiesTasks,
                    FixedAssets = @FixedAssets,
                    DueDate = @DueDate,
                    VendorID = @VendorID,
                    ProposalNumber = @ProposalNumber,
                    StatusID = (
                        SELECT TOP 1 StatusID
                        FROM [CRManagementDB].[dbo].[CRStatus]
                        WHERE StatusName = @TargetStatusName
                          AND Active = 1
                    )
                WHERE CRID = @CRID
                  AND Active = 1";

            var crParameters = new DynamicParameters();

            crParameters.Add("@ActivitiesTasks", activitiesTasks);
            crParameters.Add("@FixedAssets", fixedAssetInfo);
            crParameters.Add("@DueDate", targetDate);

            if (string.IsNullOrWhiteSpace(vendorID))
                crParameters.Add("@VendorID", null);
            else
                crParameters.Add("@VendorID", vendorID);

            crParameters.Add("@ProposalNumber", proposalNumber);
            crParameters.Add("@TargetStatusName", targetStatusName);
            crParameters.Add("@CRID", crId);

            _connectionService.ExecuteWithPara(updateCrSql, crParameters);


            // ---------------------------------------------------------
            // Save SRS Attachments
            // ---------------------------------------------------------
            await SaveAttachmentGroupAsync(
                crId,
                collection,
                "srsFile",
                "SRS",
                userName);


            // ---------------------------------------------------------
            // Save BA Attachments
            // ---------------------------------------------------------
            await SaveAttachmentGroupAsync(
                crId,
                collection,
                "baFile",
                "BA",
                userName);


            // ---------------------------------------------------------
            // Save Vendor Attachments
            // ---------------------------------------------------------
            await SaveAttachmentGroupAsync(
                crId,
                collection,
                "vendorFiles",
                "Vendor Report",
                userName);


            // ---------------------------------------------------------
            // On Submit only:
            // Create Development/Security workflow approval
            // ---------------------------------------------------------
            if (isSubmit)
            {
                await InsertDevelopmentApprovalStepAsync(
                    crId,
                    empId);
            }

            return true;
        }

        private async Task SaveAttachmentGroupAsync(
            int crId,
            IFormCollection collection,
            string fieldName,
            string attachmentTypeName,
            string uploadedBy)
        {
            var files = collection.Files
                .Where(f =>
                    f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase) &&
                    f.Length > 0)
                .ToList();

            if (files.Count == 0)
                return;

            int attachmentTypeId = GetAttachmentTypeId(attachmentTypeName);

            await SaveAttachmentsAsync(
                crId,
                files,
                attachmentTypeId,
                uploadedBy);
        }

        private int GetAttachmentTypeId(string attachmentTypeName)
        {
            const string sql = @"
                SELECT TOP 1 AttachmentTypeID
                FROM [CRManagementDB].[dbo].[AttachmentType]
                WHERE AttachmentName = @AttachmentName
                  AND Active = 1";

            var parameters = new DynamicParameters();
            parameters.Add("@AttachmentName", attachmentTypeName);

            var table = _connectionService.ReturnWithPara(sql, parameters);

            if (table == null || table.Rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Attachment type '{attachmentTypeName}' was not found or is inactive.");
            }

            return table.Rows[0].Field<int>("AttachmentTypeID");
        }
        private async Task InsertDevelopmentApprovalStepAsync(int crId, string empId)
        {
            // ---- 1. Get the StepID of "Developer Documentation" ----
            const string getStepIdSql = @"
                SELECT TOP (1) StepID
                FROM WorkflowStep
                WHERE StepName = @StepName
                  AND Active = 1";

            var stepParams = new DynamicParameters();
            stepParams.Add("@StepName", "Developer Documentation");

            var stepTable = _connectionService.ReturnWithPara(getStepIdSql, stepParams);
            if (stepTable == null || stepTable.Rows.Count == 0)
                throw new InvalidOperationException("Workflow step 'Developer Documentation' is not configured.");

            var assessmentStepId = stepTable.Rows[0].Field<int>("StepID");

            // ---- 1b. Get the StepID of "Division Head Approval" ----
            var divisionHeadStepParams = new DynamicParameters();
            divisionHeadStepParams.Add("@StepName", "Division Head Approval");

            var divisionHeadStepTable = _connectionService.ReturnWithPara(getStepIdSql, divisionHeadStepParams);
            if (divisionHeadStepTable == null || divisionHeadStepTable.Rows.Count == 0)
                throw new InvalidOperationException("Workflow step 'Division Head Approval' is not configured.");

            var divisionHeadStepId = divisionHeadStepTable.Rows[0].Field<int>("StepID");

            // ---- 2. Find who assigned the developer (the approver) ----
            const string getApproverSql = @"
                SELECT TOP (1) AssignedBy
                FROM Approval
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND Active = 1
                ORDER BY ApprovalID ASC";

            var approverParams = new DynamicParameters();
            approverParams.Add("@CRID", crId);
            approverParams.Add("@StepID", assessmentStepId);

            var approverTable = _connectionService.ReturnWithPara(getApproverSql, approverParams);
            if (approverTable == null || approverTable.Rows.Count == 0)
                throw new InvalidOperationException(
                    $"No Developer Documentation approval record found for CR {crId}, so the approver could not be determined.");

            var approverUsername = approverTable.Rows[0].Field<string>("AssignedBy");
            if (string.IsNullOrWhiteSpace(approverUsername))
                throw new InvalidOperationException(
                    $"The Developer Documentation approval for CR {crId} has no AssignedBy user.");

            // ---- 3. Close the logged-in user's pending Developer Documentation row ----
            const string findDevRowSql = @"
                SELECT TOP (1) ApprovalID
                FROM Approval
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND AssignedTo = @AssignedTo
                  AND Active = 1
                  AND IsApproved IS NULL
                ORDER BY ApprovalID DESC";

            var findParams = new DynamicParameters();
            findParams.Add("@CRID", crId);
            findParams.Add("@StepID", assessmentStepId);
            findParams.Add("@AssignedTo", empId);

            var devRowTable = _connectionService.ReturnWithPara(findDevRowSql, findParams);
            if (devRowTable == null || devRowTable.Rows.Count == 0)
                throw new InvalidOperationException(
                    $"No pending Developer Documentation row found for CR {crId} and user {empId}.");

            var devApprovalId = devRowTable.Rows[0].Field<int>("ApprovalID");

            const string completeDevRowSql = @"
                UPDATE Approval
                SET IsApproved   = 1,
                    ApprovalDate = @ApprovalDate
                WHERE ApprovalID = @ApprovalID";

            var completeParams = new DynamicParameters();
            completeParams.Add("@ApprovalDate", DateTime.Now);
            completeParams.Add("@ApprovalID", devApprovalId);

            await Task.Run(() => _connectionService.ExecuteWithPara(completeDevRowSql, completeParams));

            // ---- 4. Don't insert a second pending row if one already exists for the approver on Division Head Approval ----
            const string existsSql = @"
                SELECT TOP (1) ApprovalID
                FROM Approval
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND AssignedTo = @AssignedTo
                  AND Active = 1
                  AND IsApproved IS NULL";

            var existsParams = new DynamicParameters();
            existsParams.Add("@CRID", crId);
            existsParams.Add("@StepID", divisionHeadStepId);
            existsParams.Add("@AssignedTo", approverUsername);

            var existsTable = _connectionService.ReturnWithPara(existsSql, existsParams);
            if (existsTable != null && existsTable.Rows.Count > 0)
                return;

            // ---- 5. Insert the Division Head Approval row assigned back to the approver ----
            const string insertApprovalSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@StepID", divisionHeadStepId);
            parameters.Add("@AssignedBy", empId);
            parameters.Add("@AssignedTo", approverUsername);
            parameters.Add("@AssignedDate", DateTime.Now);
            parameters.Add("@Active", true);

            await Task.Run(() => _connectionService.ExecuteWithPara(insertApprovalSql, parameters));
        }

        private async Task SaveAttachmentsAsync(
            int crId,
            List<IFormFile> files,
            int attachmentTypeId,
            string uploadedBy)
        {
            if (string.IsNullOrWhiteSpace(uploadedBy))
            {
                throw new ArgumentException(
                    "Session expired. Please log in again before uploading attachments.");
            }

            if (attachmentTypeId <= 0)
            {
                throw new ArgumentException("Invalid attachment type.");
            }

            if (files == null || files.Count == 0)
            {
                return;
            }

            var rootPath = Path.Combine(
                _attachmentRootFolder,
                crId.ToString());

            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            // IMPORTANT:
            // AttachmentTypeID was missing from your previous INSERT.
            const string insertAttachmentSql = @"
                INSERT INTO [CRManagementDB].[dbo].[Attachment]
                (
                    CRID,
                    AttachmentTypeID,
                    FileName,
                    FilePath,
                    UploadedBy,
                    UploadedDate,
                    Active
                )
                VALUES
                (
                    @CRID,
                    @AttachmentTypeID,
                    @FileName,
                    @FilePath,
                    @UploadedBy,
                    @UploadedDate,
                    @Active
                )";

            foreach (var formFile in files)
            {
                if (formFile == null || formFile.Length == 0)
                    continue;

                var safeFileName =
                    Path.GetFileNameWithoutExtension(formFile.FileName);

                var extension =
                    Path.GetExtension(formFile.FileName);

                var uniqueFileName =
                    $"{safeFileName}_{DateTime.Now:yyyyMMddHHmmssfff}{extension}";

                var fullPath =
                    Path.Combine(rootPath, uniqueFileName);

                // Save physical file
                using (var stream = new FileStream(
                    fullPath,
                    FileMode.Create))
                {
                    await formFile.CopyToAsync(stream);
                }

                // Save database record
                var attachmentParameters = new DynamicParameters();

                attachmentParameters.Add("@CRID", crId);
                attachmentParameters.Add("@AttachmentTypeID", attachmentTypeId);
                attachmentParameters.Add("@FileName", formFile.FileName);
                attachmentParameters.Add("@FilePath", fullPath);
                attachmentParameters.Add("@UploadedBy", uploadedBy);
                attachmentParameters.Add("@UploadedDate", DateTime.Now);
                attachmentParameters.Add("@Active", true);

                _connectionService.ExecuteWithPara(
                    insertAttachmentSql,
                    attachmentParameters);
            }
        }
        public async Task<bool> DeleteAttachmentAsync(int attachmentId, string deletedByEmpId)
        {
            if (attachmentId <= 0)
                throw new ArgumentException("Invalid attachment.");

            // Fetch first so we know the file path to remove from disk.
            var attachment = await GetAttachmentByIdAsync(attachmentId);
            if (attachment == null)
                return false;

            const string updateSql = @"
                UPDATE [CRManagementDB].[dbo].[Attachment]
                SET Active = 0,
                    DeletedBy = @DeletedBy
                WHERE AttachmentID = @AttachmentID
                  AND Active = 1";

            var parameters = new DynamicParameters();
            parameters.Add("@AttachmentID", attachmentId);
            parameters.Add("@DeletedBy", deletedByEmpId);

            int rowsAffected = _connectionService.ExecuteWithPara(updateSql, parameters);

            if (rowsAffected <= 0)
                return false;

            try
            {
                if (System.IO.File.Exists(attachment.FilePath))
                {
                    System.IO.File.Delete(attachment.FilePath);
                }
            }
            catch
            {
            }

            return true;
        }


        public async Task<bool> CreateAssessmentSecurityAsync(int crId, IFormCollection collection, string userName, string empId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            var statusValue = collection["Status"].ToString();
            bool isSubmit = statusValue == "2";

            var targetStatusName = isSubmit ? "SecurityApproved" : "SecurityDraft";

            var riskAssessment = collection["RiskAssessment"].ToString().Trim();
            var securityRequirements = collection["SecurityRequirements"].ToString().Trim();
            var securityRisksIdentified = collection["SecurityRisksIdentified"].ToString().Trim();
            var riskTreatmentControls = collection["RiskTreatmentControls"].ToString().Trim();
            var dependenciesImpact = collection["DependenciesImpact"].ToString().Trim();

            int? changeImpactId = int.TryParse(collection["ImpactID"], out var parsedImpactId)
                ? parsedImpactId
                : (int?)null;

            if (isSubmit)
            {
                if (string.IsNullOrWhiteSpace(riskAssessment))
                    throw new ArgumentException("Please fill out the Information Security Risk Assessment.");
                if (string.IsNullOrWhiteSpace(securityRequirements))
                    throw new ArgumentException("Please fill out the Security Requirements.");
                if (string.IsNullOrWhiteSpace(securityRisksIdentified))
                    throw new ArgumentException("Please fill out the Security Risks Identified.");
                if (string.IsNullOrWhiteSpace(riskTreatmentControls))
                    throw new ArgumentException("Please fill out the Risk Treatment Controls.");
                if (string.IsNullOrWhiteSpace(dependenciesImpact))
                    throw new ArgumentException("Please fill out the Dependencies Impact.");
                if (changeImpactId == null)
                    throw new ArgumentException("Please select a Change Impact.");

                if (string.IsNullOrWhiteSpace(empId))
                    throw new ArgumentException("Employee number is required.");
            }

            empId = (empId ?? string.Empty).Trim();

            if (!isSubmit)
            {
                // Draft save: just persist the fields and status, no workflow step movement.
                const string getStatusIdSql = @"
            SELECT TOP 1 StatusID
            FROM [CRManagementDB].[dbo].[CRStatus]
            WHERE StatusName = @StatusName AND Active = 1";

                var statusIdObj = _connectionService.ExecuteScalar(getStatusIdSql, new { StatusName = targetStatusName });
                if (statusIdObj == null || statusIdObj == DBNull.Value)
                    throw new InvalidOperationException($"Status '{targetStatusName}' was not found or is inactive in CRStatus table.");

                int draftStatusId = Convert.ToInt32(statusIdObj);

                const string updateCrSql = @"
            UPDATE ChangeRequest
            SET RiskAssessment = @RiskAssessment,
                SecurityRequirements = @SecurityRequirements,
                SecurityRisksIdentified = @SecurityRisksIdentified,
                RiskTreatmentControls = @RiskTreatmentControls,
                DependenciesImpact = @DependenciesImpact,
                ImpactID = @ImpactID,
                StatusID = @StatusID
            WHERE CRID = @CRID AND Active = 1";

                var draftParams = new DynamicParameters();
                draftParams.Add("@RiskAssessment", riskAssessment);
                draftParams.Add("@SecurityRequirements", securityRequirements);
                draftParams.Add("@SecurityRisksIdentified", securityRisksIdentified);
                draftParams.Add("@RiskTreatmentControls", riskTreatmentControls);
                draftParams.Add("@DependenciesImpact", dependenciesImpact);
                draftParams.Add("@ImpactID", changeImpactId, DbType.Int32);
                draftParams.Add("@StatusID", draftStatusId);
                draftParams.Add("@CRID", crId);

                int draftRows = _connectionService.ExecuteWithPara(updateCrSql, draftParams);
                return draftRows > 0;
            }

            // Submit path: save fields + close Security step + hand back to the original Division Head approver.
            const string sql = @"
                DECLARE @SecurityStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Security' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DivHeadStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Division Head Approval' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @SecurityApprovedStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'SecurityApproved' AND Active = 1);

                IF @SecurityStepID IS NULL OR @DivHeadStepID IS NULL OR @SecurityApprovedStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @LatestSecurityApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @SecurityStepID AND Active = 1
                    ORDER BY ApprovalID DESC);

                IF @LatestSecurityApprovalID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @DivHeadAssignedTo NVARCHAR(200) = (
                    SELECT TOP 1 AssignedTo FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DivHeadStepID AND Active = 1
                    ORDER BY AssignedDate ASC);

                IF @DivHeadAssignedTo IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET RiskAssessment = @RiskAssessment,
                    SecurityRequirements = @SecurityRequirements,
                    SecurityRisksIdentified = @SecurityRisksIdentified,
                    RiskTreatmentControls = @RiskTreatmentControls,
                    DependenciesImpact = @DependenciesImpact,
                    ImpactID = @ImpactID,
                    StatusID = @SecurityApprovedStatusID
                WHERE CRID = @CRID AND Active = 1;

                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved   = 1,
                    ApprovalDate = @Now
                WHERE ApprovalID = @LatestSecurityApprovalID;

                INSERT INTO [CRManagementDB].[dbo].[Approval]
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @DivHeadStepID, @EmpID, @DivHeadAssignedTo, @Now, 1);

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpID", empId);
            parameters.Add("@RiskAssessment", riskAssessment);
            parameters.Add("@SecurityRequirements", securityRequirements);
            parameters.Add("@SecurityRisksIdentified", securityRisksIdentified);
            parameters.Add("@RiskTreatmentControls", riskTreatmentControls);
            parameters.Add("@DependenciesImpact", dependenciesImpact);
            parameters.Add("@ImpactID", changeImpactId, DbType.Int32);
            parameters.Add("@Now", DateTime.Now);

            var success = _connectionService.ExecuteScalar(sql, parameters);

            return success != null && success != DBNull.Value && Convert.ToBoolean(success);
        }

        public Task<IEnumerable<ChangeRequest>> GetChangeImpactsAsync()
        {
            const string sql = @"
                SELECT ImpactID   AS ImpactID,
                       ImpactName AS ChangeImpactName
                FROM [CRManagementDB].[dbo].[ChangeImpact]
                WHERE Active = 1
                ORDER BY ImpactID";

            var result = _connectionService.Query<ChangeRequest>(sql, new { })
                            ?? Enumerable.Empty<ChangeRequest>();

            return Task.FromResult(result);
        }

        public async Task<bool> RejectAssessmentSecurityAsync(int crId, IFormCollection collection, string userName, string empId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            var rejectReason = collection["RejectReason"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(rejectReason))
                throw new ArgumentException("Please provide a reason for rejecting this assessment.");

            if (string.IsNullOrWhiteSpace(empId))
                throw new ArgumentException("Employee number is required.");

            empId = empId.Trim();

            const string sql = @"
                DECLARE @SecurityStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Security' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DevDocStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Developer Documentation' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @SecurityRejectedStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'SecurityRejected' AND Active = 1);

                IF @SecurityStepID IS NULL OR @DevDocStepID IS NULL OR @SecurityRejectedStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @DevDocAssignedTo NVARCHAR(200) = (
                    SELECT TOP 1 AssignedTo FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DevDocStepID AND Active = 1
                    ORDER BY AssignedDate ASC);

                IF @DevDocAssignedTo IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved   = 0,
                    ApprovalDate = @Now,
                    Decision     = 'Rejected',
                    Comments     = @RejectReason
                WHERE CRID = @CRID AND StepID = @SecurityStepID AND Active = 1
                  AND IsApproved IS NULL;

                INSERT INTO [CRManagementDB].[dbo].[Approval]
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @DevDocStepID, @EmpID, @DevDocAssignedTo, @Now, 1);

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @SecurityRejectedStatusID
                WHERE CRID = @CRID AND Active = 1;

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpID", empId);
            parameters.Add("@RejectReason", rejectReason);
            parameters.Add("@Now", DateTime.Now);

            var success = _connectionService.ExecuteScalar(sql, parameters);

            return success != null && success != DBNull.Value && Convert.ToBoolean(success);
        }
        public async Task<bool> CreateTestingAsync(int crId, IFormCollection collection, string userName, string empId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            var statusValue = collection["Status"].ToString();
            bool isPass = statusValue == "2";
            bool isFail = statusValue == "3";

            if (!isPass && !isFail)
                throw new ArgumentException("Invalid action. Please choose Test Passed or Test Failed.");

            var testResult = collection["Test"].ToString().Trim();

            if (isPass && string.IsNullOrWhiteSpace(testResult))
                throw new ArgumentException("Please enter the Test Plan and Test Results.");

            var files = collection.Files.GetFiles("TestResultFiles");
            var allowedExt = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
             ".png", ".jpg", ".jpeg", ".gif", ".webp", ".zip" };
            const long maxBytes = 10 * 1024 * 1024;

            if (files.Count > 10)
                throw new ArgumentException("You can upload a maximum of 10 files.");

            foreach (var file in files)
            {
                if (file.Length == 0) continue;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!allowedExt.Contains(ext))
                    throw new ArgumentException($"File type not allowed: {file.FileName}");

                if (file.Length > maxBytes)
                    throw new ArgumentException($"File is larger than 10 MB: {file.FileName}");
            }

            const string passNextStepName = "Division Head Approval";
            const string failNextStepName = "Developer Implementation";

            const string passTargetStatusName = "TestingApproved";
            const string failTargetStatusName = "QAReturn";

            const string sql = @"
                DECLARE @TestingStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Testing' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @NextStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = @NextStepName AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @TargetStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = @TargetStatusName AND Active = 1);

                IF @TestingStepID IS NULL OR @NextStepID IS NULL OR @TargetStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @TestID INT = (
                    SELECT TOP 1 TestID FROM [CRManagementDB].[dbo].[Testing]
                    WHERE CRID = @CRID AND Active = 1 AND IsPassed IS NULL
                    ORDER BY TestCycleNumber DESC);

                IF @TestID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                -- The one OPEN Testing approval row (IsApproved IS NULL), not just the newest by ID.
                DECLARE @TestingApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @TestingStepID AND Active = 1
                      AND IsApproved IS NULL
                    ORDER BY ApprovalID DESC);

                IF @TestingApprovalID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                -- Who gets the next step:
                -- Pass  -> the ORIGINAL Division Head Approval assignee for this CR (the very first
                --          Division Head Approval row, earliest ApprovalID) — the person who started
                --          the whole approval flow, for final sign-off. NOT the Testing row's AssignedBy,
                --          which may just be whoever last handed the CR to the tester (e.g. the developer).
                -- Fail  -> the person who was AssignedTo on the last Developer Implementation step (the developer)
                DECLARE @NextAssignedTo NVARCHAR(100);

                IF @IsPassed = 1
                BEGIN
                    DECLARE @DivHeadStepID INT = (
                        SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                        WHERE StepName = 'Division Head Approval' AND Active = 1
                        ORDER BY StepOrder ASC);

                    SELECT TOP 1 @NextAssignedTo = AssignedTo
                    FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DivHeadStepID AND Active = 1
                    ORDER BY ApprovalID ASC;
                END
                ELSE
                BEGIN
                    DECLARE @DevImplStepID INT = (
                        SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                        WHERE StepName = 'Developer Implementation' AND Active = 1
                        ORDER BY StepOrder ASC);

                    SELECT TOP 1 @NextAssignedTo = AssignedTo
                    FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DevImplStepID AND Active = 1
                    ORDER BY ApprovalID DESC;
                END

                IF @NextAssignedTo IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                UPDATE [CRManagementDB].[dbo].[Testing]
                SET TestResult = @TestResult,
                    TestingDate = @Now,
                    IsPassed = @IsPassed,
                    ReturnedFrom = @EmpId
                WHERE TestID = @TestID AND Active = 1;

                -- Resolve the row we're actually acting on.
                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved = @IsPassed,
                    ApprovalDate = @Now,
                    Decision = @Decision
                WHERE ApprovalID = @TestingApprovalID;

                -- Safety net: close out ANY other still-open Testing row for this CR
                -- (e.g. an older duplicate that was never resolved) so nothing is left
                -- dangling with IsApproved/ApprovalDate = NULL once this step moves on.
                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved = @IsPassed,
                    ApprovalDate = @Now,
                    Decision = 'Assigned'
                WHERE CRID = @CRID AND StepID = @TestingStepID AND Active = 1
                  AND IsApproved IS NULL
                  AND ApprovalID <> @TestingApprovalID;

                INSERT INTO [CRManagementDB].[dbo].[Approval]
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active, Comments)
                VALUES
                    (@CRID, @NextStepID, @EmpId, @NextAssignedTo, @Now, 1, 'v3-divhead-fix');

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @TargetStatusID
                WHERE CRID = @CRID AND Active = 1;

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpId", empId);
            parameters.Add("@TestResult", testResult);
            parameters.Add("@IsPassed", isPass);
            parameters.Add("@Decision", isPass ? "Test Passed" : "Test Failed");
            parameters.Add("@NextStepName", isPass ? passNextStepName : failNextStepName);
            parameters.Add("@TargetStatusName", isPass ? passTargetStatusName : failTargetStatusName);
            parameters.Add("@Now", DateTime.Now);

            var successObj = _connectionService.ExecuteScalar(sql, parameters);
            bool success = successObj != null && successObj != DBNull.Value && Convert.ToBoolean(successObj);

            if (!success)
                return false;

            await SaveAttachmentGroupAsync(
                crId,
                collection,
                "TestResultFiles",
                "QA Report",
                userName);

            return true;
        }

        public async Task<bool> AssignFinalApproverAsync(int crId, int approverId, string approvedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");
            if (approverId <= 0)
                throw new ArgumentException("Please select a valid user to assign.");

            const string sql = @"
                DECLARE @DeptHeadStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Division Head Approval' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @FinalApprovalStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Final Approval' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @TargetStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'FinalApprovalPending' AND Active = 1);

                IF @DeptHeadStepID IS NULL OR @FinalApprovalStepID IS NULL OR @TargetStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @DeptHeadApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DeptHeadStepID AND Active = 1 AND IsApproved IS NULL
                    ORDER BY ApprovalID DESC);

                BEGIN TRANSACTION;

                IF @DeptHeadApprovalID IS NOT NULL
                BEGIN
                    UPDATE [CRManagementDB].[dbo].[Approval]
                    SET IsApproved = 1,
                        Decision = 'Division Head Approved',
                        ApprovalDate = @Now
                    WHERE ApprovalID = @DeptHeadApprovalID;
                END

                INSERT INTO [CRManagementDB].[dbo].[Approval]
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @FinalApprovalStepID, @ApprovedByEmpId, @ApproverID, @Now, 1);

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @TargetStatusID
                WHERE CRID = @CRID AND Active = 1;

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@ApproverID", approverId);
            parameters.Add("@ApprovedByEmpId", approvedByEmpId);
            parameters.Add("@Now", DateTime.Now);

            var successObj = _connectionService.ExecuteScalar(sql, parameters);

            return successObj != null && successObj != DBNull.Value && Convert.ToBoolean(successObj);
        }
        public async Task<bool> AcceptDevAssessment(int crId, string empNo, string securityOfficerId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            static string CleanId(string? v)
            {
                v = (v ?? string.Empty).Trim();
                var slash = v.LastIndexOf('\\');
                if (slash >= 0) v = v.Substring(slash + 1);
                var dash = v.IndexOf(" - ", StringComparison.Ordinal);
                if (dash >= 0) v = v.Substring(0, dash);
                return v.Trim();
            }

            empNo = CleanId(empNo);
            securityOfficerId = CleanId(securityOfficerId);

            if (string.IsNullOrWhiteSpace(empNo))
                throw new ArgumentException("The logged-in user could not be determined.");
            if (string.IsNullOrWhiteSpace(securityOfficerId))
                throw new ArgumentException("Please select an IS Officer.");

            // ---- 1. StatusID of "AssessmentAccepted" ----
            const string getStatusIdSql = @"
                SELECT TOP (1) StatusID
                FROM [CRManagementDB].[dbo].[CRStatus]
                WHERE StatusName = @StatusName
                  AND Active = 1";

            var statusLookup = new DynamicParameters();
            statusLookup.Add("@StatusName", "AssessmentAccepted");

            var statusTable = _connectionService.ReturnWithPara(getStatusIdSql, statusLookup);
            if (statusTable == null || statusTable.Rows.Count == 0)
                throw new InvalidOperationException(
                    "Status 'AssessmentAccepted' was not found or is inactive in the CRStatus table.");

            int acceptedStatusId = Convert.ToInt32(statusTable.Rows[0]["StatusID"]);

            // ---- 2. StepIDs (exact name first, then a LIKE fallback) ----
            int GetStepId(string stepName)
            {
                const string sql = @"
                    SELECT TOP (1) StepID
                    FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE Active = 1
                      AND (StepName = @StepName OR StepName LIKE @StepLike)
                    ORDER BY CASE WHEN StepName = @StepName THEN 0 ELSE 1 END, StepOrder ASC";

                var p = new DynamicParameters();
                p.Add("@StepName", stepName);
                p.Add("@StepLike", "%" + stepName + "%");

                var table = _connectionService.ReturnWithPara(sql, p);
                if (table == null || table.Rows.Count == 0)
                    throw new InvalidOperationException($"Workflow step '{stepName}' was not found or is inactive.");

                return Convert.ToInt32(table.Rows[0]["StepID"]);
            }

            int devDocStepId = GetStepId("Division Head Approval");
            int securityStepId = GetStepId("Security");

            // ---- 3. The logged-in user's pending Developer Documentation row ----
            const string findPendingSql = @"
                SELECT TOP (1) ApprovalID
                FROM Approval
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND AssignedTo = @AssignedTo
                  AND Active = 1
                  AND IsApproved IS NULL
                  AND (Decision IS NULL OR Decision = '' OR Decision = 'Pending')
                ORDER BY ApprovalID DESC";

            var findParams = new DynamicParameters();
            findParams.Add("@CRID", crId);
            findParams.Add("@StepID", devDocStepId);
            findParams.Add("@AssignedTo", empNo);

            var pendingTable = _connectionService.ReturnWithPara(findPendingSql, findParams);
            if (pendingTable == null || pendingTable.Rows.Count == 0)
                throw new InvalidOperationException(
                    $"No pending Developer Documentation approval found for CR {crId} and user {empNo}.");

            int pendingApprovalId = Convert.ToInt32(pendingTable.Rows[0]["ApprovalID"]);

            // ---- 4. Insert the Security row for the IS Officer (skip if one is already pending) ----
            const string existsSql = @"
                SELECT TOP (1) ApprovalID
                FROM Approval
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND AssignedTo = @AssignedTo
                  AND Active = 1
                  AND IsApproved IS NULL";

            var existsParams = new DynamicParameters();
            existsParams.Add("@CRID", crId);
            existsParams.Add("@StepID", securityStepId);
            existsParams.Add("@AssignedTo", securityOfficerId);

            var existsTable = _connectionService.ReturnWithPara(existsSql, existsParams);
            bool securityRowExists = existsTable != null && existsTable.Rows.Count > 0;

            if (!securityRowExists)
            {
                const string insertSql = @"
                    INSERT INTO Approval
                        (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                    VALUES
                        (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

                var insertParams = new DynamicParameters();
                insertParams.Add("@CRID", crId);
                insertParams.Add("@StepID", securityStepId);
                insertParams.Add("@AssignedBy", empNo);
                insertParams.Add("@AssignedTo", securityOfficerId);
                insertParams.Add("@AssignedDate", DateTime.Now);
                insertParams.Add("@Active", true);

                int insertRows = await Task.Run(() => _connectionService.ExecuteWithPara(insertSql, insertParams));
                if (insertRows <= 0)
                    throw new InvalidOperationException("The Security approval row could not be created.");
            }

            // ---- 5. Close the logged-in user's Developer Documentation row as accepted ----
            const string closeRowSql = @"
                UPDATE Approval
                SET IsApproved   = 1,
                    Decision     = @Decision,
                    ApprovalDate = @ApprovalDate
                WHERE ApprovalID = @ApprovalID";

            var closeParams = new DynamicParameters();
            closeParams.Add("@Decision", "Dev Docs Accepted");
            closeParams.Add("@ApprovalDate", DateTime.Now);
            closeParams.Add("@ApprovalID", pendingApprovalId);

            await Task.Run(() => _connectionService.ExecuteWithPara(closeRowSql, closeParams));

            // ---- 6. Set the Change Request status to AssessmentAccepted (looked up by name above) ----
            const string updateStatusSql = @"
                UPDATE ChangeRequest
                SET StatusID = @StatusID
                WHERE CRID = @CRID";

            var statusParams = new DynamicParameters();
            statusParams.Add("@StatusID", acceptedStatusId);
            statusParams.Add("@CRID", crId);

            int statusRows = await Task.Run(() => _connectionService.ExecuteWithPara(updateStatusSql, statusParams));
            return statusRows > 0;
        }

        public async Task<bool> ReturnDevAssessment(int crId, string empNo)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            if (string.IsNullOrWhiteSpace(empNo))
                throw new ArgumentException("Employee number is required.");

            empNo = empNo.Trim();

            const string sql = @"
                DECLARE @DivHeadStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Division Head Approval' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DevDocStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Developer Documentation' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @AssessmentReturnedStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'AssessmentReturned' AND Active = 1);

                IF @DivHeadStepID IS NULL OR @DevDocStepID IS NULL OR @AssessmentReturnedStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @ApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DivHeadStepID
                      AND Active = 1 AND IsApproved IS NULL
                      AND (Decision IS NULL OR Decision = '' OR Decision = 'Pending')
                      AND (AssignedTo = @EmpNo
                           OR AssignedTo LIKE @EmpNo + ' - %'
                           OR AssignedTo LIKE '%\' + @EmpNo)
                    ORDER BY ApprovalID DESC);

                IF @ApprovalID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @DevUser NVARCHAR(200) = (
                    SELECT TOP 1 AssignedBy FROM [CRManagementDB].[dbo].[Approval]
                    WHERE ApprovalID = @ApprovalID);

                IF @DevUser IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved   = 0,
                    Decision     = 'Returned',
                    ApprovalDate = @Now
                WHERE ApprovalID = @ApprovalID;

                INSERT INTO [CRManagementDB].[dbo].[Approval]
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @DevDocStepID, @EmpNo, @DevUser, @Now, 1);

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @AssessmentReturnedStatusID
                WHERE CRID = @CRID AND Active = 1;

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpNo", empNo);
            parameters.Add("@Now", DateTime.Now);

            var success = _connectionService.ExecuteScalar(sql, parameters);

            return success != null && success != DBNull.Value && Convert.ToBoolean(success);
        }

        public async Task<bool> ApproveToDevelopmentAsync(int crId, string empNo)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            const string sql = @"
                DECLARE @DeptHeadStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Division Head Approval' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DevDocStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Developer Documentation' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DevImplStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Developer Implementation' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DevelopmentApprovedStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'DevelopmentApproved' AND Active = 1);

                IF @DeptHeadStepID IS NULL OR @DevDocStepID IS NULL OR @DevImplStepID IS NULL OR @DevelopmentApprovedStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @ApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DeptHeadStepID AND Active = 1
                    ORDER BY ApprovalID DESC);

                IF @ApprovalID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @DeveloperUser NVARCHAR(100) = (
                    SELECT TOP 1 AssignedTo FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DevDocStepID AND Active = 1
                    ORDER BY ApprovalID ASC);

                IF @DeveloperUser IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved = 1,
                    ApprovalDate = @Now,
                    Decision = 'Approved to Development'
                WHERE ApprovalID = @ApprovalID;

                INSERT INTO [CRManagementDB].[dbo].[Approval]
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @DevImplStepID, @EmpNo, @DeveloperUser, @Now, 1);

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @DevelopmentApprovedStatusID
                WHERE CRID = @CRID AND Active = 1;

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpNo", empNo);
            parameters.Add("@Now", DateTime.Now);

            var success = _connectionService.ExecuteScalar(sql, parameters);

            return success != null && success != DBNull.Value && Convert.ToBoolean(success);
        }

        public async Task<bool> ProceedToDevelopmentAsync(int crId, string uatLink, string empNo)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            if (string.IsNullOrWhiteSpace(uatLink))
                throw new ArgumentException("Please provide UAT Link.");

            if (string.IsNullOrWhiteSpace(empNo))
                throw new ArgumentException("Employee number is required.");

            uatLink = uatLink.Trim();

            const string sql = @"
                DECLARE @DevImplStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Developer Implementation' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @TestingStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Testing' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DevReturnStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'DevReturn' AND Active = 1);

                IF @DevImplStepID IS NULL OR @TestingStepID IS NULL OR @DevReturnStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                -- The currently OPEN Developer Implementation approval row for this developer
                -- (the one waiting on them right now — not any earlier, already-approved row).
                DECLARE @ApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DevImplStepID AND AssignedTo = @EmpNo
                      AND IsApproved IS NULL AND Active = 1
                    ORDER BY ApprovalID DESC);

                IF @ApprovalID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                -- Who should get the new Testing step: same tester as the most recent
                -- Testing approval row for this CR (whether that row is still open or was
                -- already resolved/returned), so it goes back to the same QA person.
                DECLARE @TesterAssignedTo NVARCHAR(100) = (
                    SELECT TOP 1 AssignedTo
                    FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @TestingStepID AND Active = 1
                    ORDER BY ApprovalID DESC);

                IF @TesterAssignedTo IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                -- Close out this Developer Implementation step as approved.
                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved = 1,
                    ApprovalDate = @Now,
                    Comments = 'UAT Link Uploaded'
                WHERE ApprovalID = @ApprovalID;

                -- Open a new Testing approval row, assigned back to the same tester.
                INSERT INTO [CRManagementDB].[dbo].[Approval]
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @TestingStepID, @EmpNo, @TesterAssignedTo, @Now, 1);

                -- Open a new Testing cycle record for the tester to fill in.
                INSERT INTO [CRManagementDB].[dbo].[Testing]
                    (CRID, TestCycleNumber, TestResult, UATLink, TestingDate, Active, IsPassed, ReturnedFrom)
                VALUES
                    (@CRID,
                     (SELECT ISNULL(MAX(TestCycleNumber), 0) + 1 FROM [CRManagementDB].[dbo].[Testing] WHERE CRID = @CRID),
                     NULL, @UATLink, NULL, 1, NULL, NULL);

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @DevReturnStatusID
                WHERE CRID = @CRID AND Active = 1;

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpNo", empNo);
            parameters.Add("@UATLink", uatLink);
            parameters.Add("@Now", DateTime.Now);

            var success = _connectionService.ExecuteScalar(sql, parameters);

            return success != null && success != DBNull.Value && Convert.ToBoolean(success);
        }

        public async Task<bool> ReturnToQAAsync(int crId, string reason, string empNo)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Please provide a reason for returning the Change Request to QA.");
            if (string.IsNullOrWhiteSpace(empNo))
                throw new ArgumentException("The logged-in user could not be determined.");

            reason = reason.Trim();

            // ---- 1. Resolve the status and the Testing step by NAME ----
            const string getStatusIdSql = @"
                SELECT TOP 1 StatusID
                FROM [CRManagementDB].[dbo].[CRStatus]
                WHERE StatusName = @StatusName AND Active = 1";

            var statusIdObj = _connectionService.ExecuteScalar(getStatusIdSql, new { StatusName = "DevReturn" });

            if (statusIdObj == null || statusIdObj == DBNull.Value)
                throw new InvalidOperationException("Status 'DevReturn' was not found or is inactive in CRStatus table.");

            int qaReadyStatusId = Convert.ToInt32(statusIdObj);

            const string getStepIdSql = @"
                SELECT TOP 1 StepID
                FROM [CRManagementDB].[dbo].[WorkflowStep]
                WHERE StepName = @StepName
                  AND Active = 1
                ORDER BY StepOrder ASC";

            var testingStepIdObj = _connectionService.ExecuteScalar(getStepIdSql, new { StepName = "Testing" });

            if (testingStepIdObj == null || testingStepIdObj == DBNull.Value)
                throw new InvalidOperationException("Workflow step 'Testing' was not found or is inactive.");

            int testingStepId = Convert.ToInt32(testingStepIdObj);

            // ---- 2. The logged-in user's pending Division Head Approval row (matched by step NAME).
            //         Its AssignedBy is the QA user who passed the test. ----
            const string findPendingSql = @"
                SELECT TOP 1 a.ApprovalID, a.AssignedBy
                FROM [CRManagementDB].[dbo].[Approval] a
                INNER JOIN [CRManagementDB].[dbo].[WorkflowStep] ws ON ws.StepID = a.StepID
                WHERE a.CRID = @CRID
                  AND ws.StepName = @StepName
                  AND a.IsApproved IS NULL
                  AND a.Active = 1
                  AND (   a.AssignedTo = @AssignedTo
                       OR a.AssignedTo LIKE @AssignedTo + ' - %'
                       OR a.AssignedTo LIKE '%\' + @AssignedTo)
                ORDER BY a.ApprovalID DESC";

            var findParams = new DynamicParameters();
            findParams.Add("@CRID", crId);
            findParams.Add("@StepName", "Division Head Approval");
            findParams.Add("@AssignedTo", empNo);

            var pendingTable = _connectionService.ReturnWithPara(findPendingSql, findParams);

            if (pendingTable == null || pendingTable.Rows.Count == 0)
                throw new InvalidOperationException(
                    $"No pending Division Head Approval row found for CR {crId} and user {empNo}.");

            int pendingApprovalId = Convert.ToInt32(pendingTable.Rows[0]["ApprovalID"]);
            string qaUser = Convert.ToString(pendingTable.Rows[0]["AssignedBy"]);

            if (string.IsNullOrWhiteSpace(qaUser))
                throw new InvalidOperationException($"The QA user for CR {crId} could not be determined.");

            // ---- 3. Update the CR status ----
            const string updateStatusSql = @"
                UPDATE ChangeRequest
                SET StatusID = @StatusID
                WHERE CRID = @CRID
                  AND Active = 1";

            var statusParams = new DynamicParameters();
            statusParams.Add("@StatusID", qaReadyStatusId);
            statusParams.Add("@CRID", crId);

            int statusRows = await Task.Run(() => _connectionService.ExecuteWithPara(updateStatusSql, statusParams));

            if (statusRows <= 0)
                return false;

            // ---- 4. Close the division head's pending row as returned (reason in Comments) ----
            const string closeRowSql = @"
                UPDATE Approval
                SET IsApproved   = 0,
                    Decision     = @Decision,
                    Comments     = @Comments,
                    ApprovalDate = @ApprovalDate
                WHERE ApprovalID = @ApprovalID";

            var closeParams = new DynamicParameters();
            closeParams.Add("@Decision", "Returned to QA");
            closeParams.Add("@Comments", reason);
            closeParams.Add("@ApprovalDate", DateTime.Now);
            closeParams.Add("@ApprovalID", pendingApprovalId);

            await Task.Run(() => _connectionService.ExecuteWithPara(closeRowSql, closeParams));

            // ---- 5. Close any old pending "Testing" rows (QA already finished that test),
            //         then create a fresh pending one for the retest ----
            const string closeOldTestingSql = @"
                UPDATE Approval
                SET IsApproved   = 1,
                    ApprovalDate = @ApprovalDate
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND IsApproved IS NULL
                  AND Active = 1";

            var closeOldParams = new DynamicParameters();
            closeOldParams.Add("@CRID", crId);
            closeOldParams.Add("@StepID", testingStepId);
            closeOldParams.Add("@ApprovalDate", DateTime.Now);

            await Task.Run(() => _connectionService.ExecuteWithPara(closeOldTestingSql, closeOldParams));

            const string insertApprovalSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            var insertApprovalParams = new DynamicParameters();
            insertApprovalParams.Add("@CRID", crId);
            insertApprovalParams.Add("@StepID", testingStepId);
            insertApprovalParams.Add("@AssignedBy", empNo);
            insertApprovalParams.Add("@AssignedTo", qaUser);
            insertApprovalParams.Add("@AssignedDate", DateTime.Now);
            insertApprovalParams.Add("@Active", true);

            int approvalRows = await Task.Run(() => _connectionService.ExecuteWithPara(insertApprovalSql, insertApprovalParams));

            if (approvalRows <= 0)
                return false;

            // ---- 6. New Testing record for the retest: next cycle, same UAT link, no result yet ----
            // 8 columns -> 8 values (the column order below must match the value order)
            const string insertTestingSql = @"
                INSERT INTO [CRManagementDB].[dbo].[Testing]
                    (CRID, TestCycleNumber, TestResult, UATLink, TestingDate, Active, IsPassed, ReturnedFrom)
                SELECT
                    @CRID,                                   -- CRID
                    ISNULL(MAX(TestCycleNumber), 0) + 1,     -- TestCycleNumber
                    NULL,                                    -- TestResult
                    (SELECT TOP 1 UATLink
                     FROM [CRManagementDB].[dbo].[Testing]
                     WHERE CRID = @CRID
                     ORDER BY TestCycleNumber DESC),         -- UATLink
                    NULL,                                    -- TestingDate
                    1,                                       -- Active
                    NULL,                                    -- IsPassed
                    NULL                                     -- ReturnedFrom
                FROM [CRManagementDB].[dbo].[Testing]
                WHERE CRID = @CRID";

            var testingParams = new DynamicParameters();
            testingParams.Add("@CRID", crId);

            int testingRows = await Task.Run(() => _connectionService.ExecuteWithPara(insertTestingSql, testingParams));

            return testingRows > 0;
        }

        public async Task<bool> FinalApproval(int crId, string empNo)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            if (string.IsNullOrWhiteSpace(empNo))
                throw new ArgumentException("Employee number is required.");

            empNo = empNo.Trim();

            const string sql = @"
                DECLARE @FinalApprovalStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Final Approval' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DevDocStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Developer Documentation' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @ReleaseClosureStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Release Closure' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @FinalApprovedStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'FinalApproved' AND Active = 1);

                IF @FinalApprovalStepID IS NULL OR @DevDocStepID IS NULL
                   OR @ReleaseClosureStepID IS NULL OR @FinalApprovedStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @ApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @FinalApprovalStepID
                      AND Active = 1 AND IsApproved IS NULL
                      AND (Decision IS NULL OR Decision = '' OR Decision = 'Pending')
                      AND (AssignedTo = @EmpNo
                           OR AssignedTo LIKE @EmpNo + ' - %'
                           OR AssignedTo LIKE '%\' + @EmpNo)
                    ORDER BY ApprovalID DESC);

                IF @ApprovalID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @ReleaseClosureUser NVARCHAR(200) = (
                    SELECT TOP 1 AssignedTo FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @DevDocStepID AND Active = 1
                    ORDER BY ApprovalID ASC);

                IF @ReleaseClosureUser IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved   = 1,
                    Decision     = 'Final Approved',
                    ApprovalDate = @Now
                WHERE ApprovalID = @ApprovalID;

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @FinalApprovedStatusID
                WHERE CRID = @CRID AND Active = 1;

                IF NOT EXISTS (
                    SELECT 1 FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @ReleaseClosureStepID
                      AND AssignedTo = @ReleaseClosureUser
                      AND Active = 1 AND IsApproved IS NULL)
                BEGIN
                    INSERT INTO [CRManagementDB].[dbo].[Approval]
                        (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                    VALUES
                        (@CRID, @ReleaseClosureStepID, @EmpNo, @ReleaseClosureUser, @Now, 1);
                END

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpNo", empNo);
            parameters.Add("@Now", DateTime.Now);

            var success = _connectionService.ExecuteScalar(sql, parameters);

            return success != null && success != DBNull.Value && Convert.ToBoolean(success);
        }
        public async Task<bool> SubmitLiveLink(int crId, string liveLink, string empNo)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            liveLink = (liveLink ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(liveLink))
                throw new ArgumentException("Live link is required.");

            if (!Uri.TryCreate(liveLink, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Live link must be a valid http/https URL.");

            if (string.IsNullOrWhiteSpace(empNo))
                throw new ArgumentException("Employee number is required.");

            empNo = empNo.Trim();

            const string sql = @"
                DECLARE @ReleaseClosureStepID INT = (
                    SELECT TOP 1 StepID FROM [CRManagementDB].[dbo].[WorkflowStep]
                    WHERE StepName = 'Release Closure' AND Active = 1
                    ORDER BY StepOrder ASC);

                DECLARE @DeployedStatusID INT = (
                    SELECT TOP 1 StatusID FROM [CRManagementDB].[dbo].[CRStatus]
                    WHERE StatusName = 'Deployed' AND Active = 1);

                IF @ReleaseClosureStepID IS NULL OR @DeployedStatusID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                DECLARE @ApprovalID INT = (
                    SELECT TOP 1 ApprovalID FROM [CRManagementDB].[dbo].[Approval]
                    WHERE CRID = @CRID AND StepID = @ReleaseClosureStepID
                      AND Active = 1 AND IsApproved IS NULL
                      AND (Decision IS NULL OR Decision = '' OR Decision = 'Pending')
                      AND (AssignedTo = @EmpNo
                           OR AssignedTo LIKE @EmpNo + ' - %'
                           OR AssignedTo LIKE '%\' + @EmpNo)
                    ORDER BY ApprovalID DESC);

                IF @ApprovalID IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Success;
                    RETURN;
                END

                BEGIN TRANSACTION;

                DECLARE @DeploymentID INT = (
                    SELECT TOP 1 DeploymentID FROM [CRManagementDB].[dbo].[Deployment]
                    WHERE CRID = @CRID AND Active = 1
                    ORDER BY DeploymentID DESC);

                IF @DeploymentID IS NOT NULL
                BEGIN
                    UPDATE [CRManagementDB].[dbo].[Deployment]
                    SET LiveLink       = @LiveLink,
                        DeploymentDate = @Now
                    WHERE DeploymentID = @DeploymentID;
                END
                ELSE
                BEGIN
                    INSERT INTO [CRManagementDB].[dbo].[Deployment]
                        (CRID, LiveLink, DeploymentDate, DeploymentStatus, Active)
                    VALUES
                        (@CRID, @LiveLink, @Now, 'Deployed', 1);
                END

                UPDATE [CRManagementDB].[dbo].[Approval]
                SET IsApproved   = 1,
                    Decision     = 'Approved',
                    ApprovalDate = @Now
                WHERE ApprovalID = @ApprovalID;

                UPDATE [CRManagementDB].[dbo].[ChangeRequest]
                SET StatusID = @DeployedStatusID
                WHERE CRID = @CRID AND Active = 1;

                COMMIT TRANSACTION;

                SELECT CAST(1 AS BIT) AS Success;";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@EmpNo", empNo);
            parameters.Add("@LiveLink", liveLink);
            parameters.Add("@Now", DateTime.Now);

            var success = _connectionService.ExecuteScalar(sql, parameters);

            return success != null && success != DBNull.Value && Convert.ToBoolean(success);
        }

        public async Task<DashboardMetrics> GetDashboardMetricsAsync()
        {
            // Single round-trip: count all active CRs, then bucket them by CRStatus.StatusName.
            //   - ApprovedCRs : CRs currently sitting in the "Approved" status
            //   - DeployedCRs : CRs that have gone live / been closed out
            //   - RejectedCRs : CRs currently in the "Rejected" status
            // Adjust the status names in the CASE WHEN clauses below if your CRStatus table
            // uses different labels for "deployed" (e.g. just "Closed").
            const string sql = @"
                SELECT
                    COUNT(*) AS TotalCRs,
                    SUM(CASE WHEN s.StatusName = 'Approved' THEN 1 ELSE 0 END) AS ApprovedCRs,
                    SUM(CASE WHEN s.StatusName IN ('Closed', 'Released', 'Completed', 'ReleaseClosed', 'LiveLinkSubmitted')
                              THEN 1 ELSE 0 END) AS DeployedCRs,
                    SUM(CASE WHEN s.StatusName = 'Rejected' THEN 1 ELSE 0 END) AS RejectedCRs
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] s ON s.StatusID = cr.StatusID
                WHERE cr.Active = 1";

            var table = _connectionService.ReturnWithPara(sql, null);

            if (table == null || table.Rows.Count == 0)
            {
                return new DashboardMetrics();
            }

            var row = table.Rows[0];

            int SafeInt(object val) => val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);

            return await Task.FromResult(new DashboardMetrics
            {
                TotalCRs = SafeInt(row["TotalCRs"]),
                ApprovedCRs = SafeInt(row["ApprovedCRs"]),
                DeployedCRs = SafeInt(row["DeployedCRs"]),
                RejectedCRs = SafeInt(row["RejectedCRs"])
            });
        }

        public async Task<IEnumerable<DashboardMetrics>> GetSubmissionTrendAsync(string period)
        {
            period = (period ?? "month").Trim().ToLowerInvariant();

            return period switch
            {
                "week" => await GetWeeklyTrendAsync(),
                "year" => await GetYearlyTrendAsync(),
                _ => await GetMonthlyTrendAsync()
            };
        }

        // Last 8 weeks (Mon-Sun buckets), including the current week
        private async Task<IEnumerable<DashboardMetrics>> GetWeeklyTrendAsync()
        {
            const string sql = @"
                SELECT
                    DATEADD(WEEK, DATEDIFF(WEEK, 0, cr.RequestedDate), 0) AS WeekStart,
                    COUNT(*) AS Cnt
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                WHERE cr.Active = 1
                  AND cr.RequestedDate >= DATEADD(WEEK, -7, DATEADD(WEEK, DATEDIFF(WEEK, 0, GETDATE()), 0))
                GROUP BY DATEADD(WEEK, DATEDIFF(WEEK, 0, cr.RequestedDate), 0)
                ORDER BY WeekStart";

            var table = _connectionService.ReturnWithPara(sql, null);

            var counts = (table?.AsEnumerable() ?? Enumerable.Empty<DataRow>())
                .ToDictionary(
                    r => r.Field<DateTime>("WeekStart").Date,
                    r => Convert.ToInt32(r["Cnt"]));

            // Build the 8 week-start dates (Monday-based) ending with the current week
            var today = DateTime.Today;
            int diffToMonday = ((int)today.DayOfWeek + 6) % 7; // Mon=0 ... Sun=6
            var currentWeekStart = today.AddDays(-diffToMonday);

            var result = new List<DashboardMetrics>();
            for (int i = 7; i >= 0; i--)
            {
                var weekStart = currentWeekStart.AddDays(-7 * i);
                counts.TryGetValue(weekStart, out var cnt);

                result.Add(new DashboardMetrics
                {
                    Label = weekStart.ToString("MMM dd"),
                    Count = cnt
                });
            }

            return await Task.FromResult(result);
        }

        // Jan-Dec of the current year
        private async Task<IEnumerable<DashboardMetrics>> GetMonthlyTrendAsync()
        {
            const string sql = @"
                SELECT
                    MONTH(cr.RequestedDate) AS MonthNum,
                    COUNT(*) AS Cnt
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                WHERE cr.Active = 1
                  AND YEAR(cr.RequestedDate) = YEAR(GETDATE())
                GROUP BY MONTH(cr.RequestedDate)";

            var table = _connectionService.ReturnWithPara(sql, null);

            var counts = (table?.AsEnumerable() ?? Enumerable.Empty<DataRow>())
                .ToDictionary(
                    r => Convert.ToInt32(r["MonthNum"]),
                    r => Convert.ToInt32(r["Cnt"]));

            var monthNames = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

            var result = new List<DashboardMetrics>();
            for (int m = 1; m <= 12; m++)
            {
                counts.TryGetValue(m, out var cnt);
                result.Add(new DashboardMetrics
                {
                    Label = monthNames[m - 1],
                    Count = cnt
                });
            }

            return await Task.FromResult(result);
        }

        // Last 6 years including the current year
        private async Task<IEnumerable<DashboardMetrics>> GetYearlyTrendAsync()
        {
            const string sql = @"
                SELECT
                    YEAR(cr.RequestedDate) AS Yr,
                    COUNT(*) AS Cnt
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                WHERE cr.Active = 1
                  AND YEAR(cr.RequestedDate) >= YEAR(GETDATE()) - 5
                GROUP BY YEAR(cr.RequestedDate)";

            var table = _connectionService.ReturnWithPara(sql, null);

            var counts = (table?.AsEnumerable() ?? Enumerable.Empty<DataRow>())
                .ToDictionary(
                    r => Convert.ToInt32(r["Yr"]),
                    r => Convert.ToInt32(r["Cnt"]));

            int currentYear = DateTime.Today.Year;
            var result = new List<DashboardMetrics>();
            for (int y = currentYear - 5; y <= currentYear; y++)
            {
                counts.TryGetValue(y, out var cnt);
                result.Add(new DashboardMetrics
                {
                    Label = y.ToString(),
                    Count = cnt
                });
            }

            return await Task.FromResult(result);
        }


    }
}
