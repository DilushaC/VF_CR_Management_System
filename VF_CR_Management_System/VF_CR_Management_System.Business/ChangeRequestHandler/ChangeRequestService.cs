using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;

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
                WHERE StepName LIKE '%Department Head%' AND Active = 1
                ORDER BY StepOrder ASC;";

                // Use ExecuteScalar or similar helper method from your connection service
                var stepIdObj = _connectionService.ExecuteScalar(getStepIdSql);

                if (stepIdObj == null || stepIdObj == DBNull.Value)
                {
                    throw new InvalidOperationException("Workflow step for 'Department Head' approval was not found or is inactive.");
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

            // FIX: RiskAssessment and ChangeImpactID are now selected so the
            // Security Assessment view can restore a saved draft.
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
                    cr.ChangeImpactID,
                    ct.ChangeTypeName AS ChangeType,
                    cr.PriorityID,
                    p.PriorityName    AS Priority,
                    cr.DivisionID,
                    dv.DivisionName   AS Division,
                    cr.ModuleID,
                    m.ModuleName      AS Module,
                    cr.StatusID,
                    s.StatusName      AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo    AS ApproverUserName,
                    cr.RequestedDate,
                    cr.DueDate
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority]   p  ON p.PriorityID   = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division]   dv ON dv.DivisionID  = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module]     m  ON m.ModuleID    = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus]   s  ON s.StatusID    = cr.StatusID
                LEFT JOIN [dbo].[Approval] App                    ON App.CRID     = cr.CRID
                WHERE cr.Active = 1
                  AND cr.CRID = @CRID";

            var result = _connectionService.Query<ChangeRequest>(sql, new { CRID = crId });
            var changeRequest = result.FirstOrDefault();

            if (changeRequest == null)
                return null;

            var userNames = new[] { changeRequest.RequestedBy, changeRequest.ApproverUserName }
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
                        r => $"{r.Field<string?>("FirstName")} {r.Field<string?>("LastName")}".Trim(),
                        StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(changeRequest.RequestedBy) &&
                    nameLookup.TryGetValue(changeRequest.RequestedBy, out var requesterFullName) &&
                    !string.IsNullOrWhiteSpace(requesterFullName))
                {
                    changeRequest.RequestedBy = requesterFullName;
                }

                if (!string.IsNullOrWhiteSpace(changeRequest.ApproverUserName) &&
                    nameLookup.TryGetValue(changeRequest.ApproverUserName, out var approverFullName) &&
                    !string.IsNullOrWhiteSpace(approverFullName))
                {
                    changeRequest.ApproverUserName = approverFullName;
                }
            }

            return changeRequest;
        }

        public Task<IEnumerable<Attachment>> GetAttachmentsByCrIdAsync(int crId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            const string sql = @"
                SELECT
                    AttachmentID,
                    CRID,
                    FileName,
                    FilePath,
                    UploadedBy,
                    UploadedDate,
                    Active
                FROM [CRManagementDB].[dbo].[Attachment]
                WHERE CRID = @CRID
                  AND Active = 1
                ORDER BY UploadedDate DESC";

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
                    AttachmentID,
                    CRID,
                    FileName,
                    FilePath,
                    UploadedBy,
                    UploadedDate,
                    Active
                FROM [CRManagementDB].[dbo].[Attachment]
                WHERE AttachmentID = @AttachmentID
                  AND Active = 1";

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
                // 1. Fetch StepID dynamically for Department Head approval
                const string getStepIdSql = @"
                    SELECT TOP 1 StepID 
                    FROM WorkflowStep 
                    WHERE StepName LIKE '%Department Head%' AND Active = 1
                    ORDER BY StepOrder ASC;";

                var stepIdObj = _connectionService.ExecuteScalar(getStepIdSql);

                if (stepIdObj == null || stepIdObj == DBNull.Value)
                {
                    throw new InvalidOperationException("Workflow step for 'Department Head' approval was not found or is inactive.");
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

        public async Task<bool> ApproveChangeRequestAsync(int crId, int approverId, string approvedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");
            if (approverId <= 0)
                throw new ArgumentException("Please select a valid user to assign.");

            // 1. Update the current active Approval record
            const string updateApprovalSql = @"
                UPDATE Approval
                SET IsApproved = 1,
                    ApprovalDate = @ApprovalDate
                WHERE CRID = @CRID
                  AND Active = 1";

            var updateApprovalParameters = new DynamicParameters();
            updateApprovalParameters.Add("@CRID", crId);
            updateApprovalParameters.Add("@ApprovalDate", DateTime.Now);

            int approvalRowsAffected = _connectionService.ExecuteWithPara(updateApprovalSql, updateApprovalParameters);

            if (approvalRowsAffected <= 0)
                return false;

            // 2. Fetch the Approved StatusID dynamically from CRStatus
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

            // 3. Update ChangeRequest StatusID
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

            // 4. Fetch the Assessment StepID dynamically from WorkflowStep
            const string getAssessmentStepIdSql = @"
                SELECT TOP 1 StepID
                FROM [CRManagementDB].[dbo].[WorkflowStep]
                WHERE StepName LIKE '%Assessment%' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getAssessmentStepIdSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step for 'Assessment' was not found or is inactive.");
            }

            int assessmentStepId = Convert.ToInt32(stepIdObj);

            // 5. Insert new assignment record into Approval table for the Assessment step
            const string insertNextStepSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            var insertParameters = new DynamicParameters();
            insertParameters.Add("@CRID", crId);
            insertParameters.Add("@StepID", assessmentStepId);
            insertParameters.Add("@AssignedBy", approvedByEmpId);
            insertParameters.Add("@AssignedTo", approverId);
            insertParameters.Add("@AssignedDate", DateTime.Now);
            insertParameters.Add("@Active", true);

            int nextStepRowsAffected = _connectionService.ExecuteWithPara(insertNextStepSql, insertParameters);

            return nextStepRowsAffected > 0;
        }
        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsDraftsAsync(string empNo)
        {
            // 1. Fetch Rejected StatusID dynamically from CRStatus
            const string getRejectedStatusSql = @"
                SELECT TOP 1 StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE StatusName LIKE '%Reject%' AND Active = 1";

            var rejectedStatusObj = _connectionService.ExecuteScalar(getRejectedStatusSql);

            if (rejectedStatusObj == null || rejectedStatusObj == DBNull.Value)
            {
                throw new InvalidOperationException("Status 'Rejected' was not found or is inactive in CRStatus table.");
            }

            int rejectedStatusId = Convert.ToInt32(rejectedStatusObj);

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
                    AND cr.StatusID <> @RejectedStatusId
                    AND cr.RequesterUserName = @EmpNo
                ORDER BY
                    cr.CRID DESC;";
            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo,
                    RejectedStatusId = rejectedStatusId
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
        public Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsSubmissionsAsync(string empNo)
        {
            const int draftStatusId = 2;

            var sql = @"
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
                    AND App.StepID = (
                        SELECT TOP 1 StepID 
                        FROM [CRManagementDB].[dbo].[WorkflowStep] 
                        WHERE StepName LIKE '%Department Head%' AND Active = 1
                    )
                    AND cr.StatusID != 7
                    AND App.AssignedTo = @EmpNo
                ORDER BY
                    cr.CRID DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo,
                    DraftStatusId = draftStatusId
                }).ToList();

            if (!changeRequests.Any())
                return Task.FromResult<IEnumerable<ChangeRequest>>(changeRequests);

            var userNames = changeRequests
                .Select(cr => cr.RequestedBy)
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

            return Task.FromResult<IEnumerable<ChangeRequest>>(changeRequests);
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
                    AND (App.IsApproved = 0 OR App.IsApproved IS NULL)
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

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAssessmentsAsync(string empNo)
        {
            // 1. Fetch Assessment StepID dynamically from WorkflowStep
            const string getAssessmentStepSql = @"
                SELECT TOP 1 StepID 
                FROM [CRManagementDB].[dbo].[WorkflowStep] 
                WHERE StepName LIKE '%Assessment%' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getAssessmentStepSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step for 'Security' was not found or is inactive.");
            }

            int assessmentStepId = Convert.ToInt32(stepIdObj);

            const string getStatusIdsSql = @"
                SELECT StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE (StatusName LIKE '%Approved%' OR StatusName LIKE '%AssessmentDraft%' OR StatusName LIKE '%Assessment%') 
                  AND Active = 1";

            var statusTable = _connectionService.ReturnWithPara(getStatusIdsSql, null);
            var statusIds = statusTable.AsEnumerable()
                .Select(r => r.Field<int>("StatusID"))
                .ToList();

            if (!statusIds.Any())
            {
                return Enumerable.Empty<ChangeRequest>();
            }

            // 3. Query Change Requests joined with Approval
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
                    m.ModuleName AS Module,
                    s.StatusName AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo AS ApproverUserName,
                    cr.RequestedDate,
                    v.VendorName AS Vendor
                FROM [dbo].[Approval] AS App
                INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
                LEFT JOIN [CRManagementDB].[dbo].[Vendor] AS v ON v.VendorID = cr.VendorID 
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
                    StepID = assessmentStepId,
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

            // 5. Fetch attachments for all CRs in a single round-trip
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
        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsSecurityAsync(string empNo)
        {
            // 1. Fetch Assessment StepID dynamically from WorkflowStep
            const string getAssessmentStepSql = @"
                SELECT TOP 1 StepID 
                FROM [CRManagementDB].[dbo].[WorkflowStep] 
                WHERE StepName LIKE '%Security%' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getAssessmentStepSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step for 'Security' was not found or is inactive.");
            }

            int assessmentStepId = Convert.ToInt32(stepIdObj);

            const string getStatusIdsSql = @"
                SELECT StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE (StatusName LIKE '%Assessment%' OR StatusName LIKE '%SecurityDraft%') 
                  AND Active = 1";

            var statusTable = _connectionService.ReturnWithPara(getStatusIdsSql, null);
            var statusIds = statusTable.AsEnumerable()
                .Select(r => r.Field<int>("StatusID"))
                .ToList();

            if (!statusIds.Any())
            {
                return Enumerable.Empty<ChangeRequest>();
            }

            // 3. Query Change Requests joined with Approval
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
                    m.ModuleName AS Module,
                    s.StatusName AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    App.AssignedTo AS ApproverUserName,
                    cr.RequestedDate,
                    v.VendorName AS Vendor
                FROM [dbo].[Approval] AS App
                INNER JOIN [CRManagementDB].[dbo].[ChangeRequest] AS cr ON App.CRID = cr.CRID
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] AS ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority] AS p ON p.PriorityID = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division] AS d ON d.DivisionID = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module] AS m ON m.ModuleID = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus] AS s ON s.StatusID = cr.StatusID
                LEFT JOIN [CRManagementDB].[dbo].[Vendor] AS v ON v.VendorID = cr.VendorID 
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
                    StepID = assessmentStepId,
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

            // 5. Fetch attachments for all CRs in a single round-trip
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

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingAssignAsync(string empNo)
        {
            const string getDeptHeadStepSql = @"
                SELECT TOP 1 StepID 
                FROM [CRManagementDB].[dbo].[WorkflowStep] 
                WHERE StepName = 'Department Head Approval' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getDeptHeadStepSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step 'Department Head Approval' was not found or is inactive.");
            }

            int deptHeadStepId = Convert.ToInt32(stepIdObj);

            const string getTestingStatusIdSql = @"
                SELECT TOP 1 StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE StatusName = 'Testing' AND Active = 1";

            var statusIdObj = _connectionService.ExecuteScalar(getTestingStatusIdSql);

            if (statusIdObj == null || statusIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("CR status 'Testing' was not found or is inactive.");
            }

            int testingStatusId = Convert.ToInt32(statusIdObj);

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
                LEFT JOIN [CRManagementDB].[dbo].[ChangeImpact] AS ci ON ci.ImpactID = cr.ChangeImpactID
                WHERE cr.Active = 1
                    AND App.Active = 1
                    AND App.IsApproved = 1
                    AND App.StepID = @StepID
                    AND App.AssignedTo = @EmpNo
                    AND cr.StatusID = @StatusID
                ORDER BY
                    cr.CRID DESC;";

            var changeRequests = _connectionService.Query<ChangeRequest>(
                sql,
                new
                {
                    EmpNo = empNo,
                    StepID = deptHeadStepId,
                    StatusID = testingStatusId
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

        public async Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingQueueAsync(string empNo)
        {
            // 1. Fetch Assessment StepID dynamically from WorkflowStep
            const string getAssessmentStepSql = @"
                SELECT TOP 1 StepID 
                FROM [CRManagementDB].[dbo].[WorkflowStep] 
                WHERE StepName LIKE '%Testing%' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getAssessmentStepSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step for 'Security' was not found or is inactive.");
            }

            int assessmentStepId = Convert.ToInt32(stepIdObj);

            const string getStatusIdsSql = @"
                SELECT StatusID 
                FROM [CRManagementDB].[dbo].[CRStatus] 
                WHERE (StatusName LIKE '%Testing%' OR StatusName LIKE '%TestingDraft%') 
                  AND Active = 1";

            var statusTable = _connectionService.ReturnWithPara(getStatusIdsSql, null);
            var statusIds = statusTable.AsEnumerable()
                .Select(r => r.Field<int>("StatusID"))
                .ToList();

            if (!statusIds.Any())
            {
                return Enumerable.Empty<ChangeRequest>();
            }

            // 3. Query Change Requests joined with Approval
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
                LEFT JOIN [CRManagementDB].[dbo].[ChangeImpact] AS ci ON ci.ImpactID = cr.ChangeImpactID
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
                    StepID = assessmentStepId,
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

            // 5. Fetch attachments for all CRs in a single round-trip
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
            var poposalNumber = collection["ProposalNumber"].ToString();

            // "1" = Save (btn-save, data-status="1")  -> keep as AssessmentDraft
            // "2" = Submit (submitBtn, data-status="2") -> move to Development + log workflow step
            var statusValue = collection["Status"].ToString();
            bool isSubmit = statusValue == "2";

            var targetStatusName = isSubmit ? "Assessment" : "AssessmentDraft";

            // Only required when actually submitting — the SweetAlert on the client
            // already guarantees a selection before Submit posts, via preConfirm.
            var isOfficerUserName = collection["ISOfficerUserName"].ToString();
            if (isSubmit && string.IsNullOrWhiteSpace(isOfficerUserName))
            {
                throw new ArgumentException("Please select an IS Officer.");
            }

            const string updateCrSql = @"
                UPDATE ChangeRequest
                SET 
                    ActivitiesTasks = @ActivitiesTasks,
                    FixedAssets  = @FixedAssets,
                    DueDate = @DueDate,
                    VendorID = @VendorID,
                    ProposalNumber = @ProposalNumber,
                    StatusID        = (SELECT TOP 1 StatusID FROM CRStatus WHERE StatusName = @TargetStatusName)
                WHERE CRID = @CRID
                  AND Active = 1";

            var crParameters = new DynamicParameters();
            crParameters.Add("@ActivitiesTasks", activitiesTasks);
            crParameters.Add("@FixedAssets", fixedAssetInfo);
            crParameters.Add("@DueDate", targetDate);
            if (vendorID == "")
                crParameters.Add("@VendorID", null);
            else
                crParameters.Add("@VendorID", vendorID);
            crParameters.Add("@ProposalNumber", poposalNumber);
            crParameters.Add("@TargetStatusName", targetStatusName);
            crParameters.Add("@CRID", crId);

            _connectionService.ExecuteWithPara(updateCrSql, crParameters);

            // ---- Handle attachments included in the same form submission ----
            var files = collection.Files?.Where(f => f.Length > 0).ToList();
            if (files != null && files.Count > 0)
            {
                await SaveAttachmentsAsync(crId, files, userName);
            }

            // ---- On Submit only: log the "Development" workflow step in Approval ----
            if (isSubmit)
            {
                await InsertDevelopmentApprovalStepAsync(crId, empId, isOfficerUserName);
            }

            return true;
        }

        private async Task InsertDevelopmentApprovalStepAsync(int crId, string empId, string isOfficerUserName)
        {
            const string getStepIdSql = @"
                SELECT StepID
                FROM WorkflowStep
                WHERE StepName = @StepName
                  AND Active = 1";

            var stepParams = new DynamicParameters();
            stepParams.Add("@StepName", "Security");

            var stepTable = _connectionService.ReturnWithPara(getStepIdSql, stepParams);
            if (stepTable == null || stepTable.Rows.Count == 0)
                throw new InvalidOperationException("Workflow step 'Security' is not configured.");

            var developmentStepId = stepTable.Rows[0].Field<int>("StepID");

            const string insertApprovalSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            var parameters = new DynamicParameters();
            parameters.Add("@CRID", crId);
            parameters.Add("@StepID", developmentStepId);
            parameters.Add("@AssignedBy", empId);
            parameters.Add("@AssignedTo", isOfficerUserName); // now the selected IS Officer, not the implementer
            parameters.Add("@AssignedDate", DateTime.Now);
            parameters.Add("@Active", true);

            await Task.Run(() => _connectionService.ExecuteWithPara(insertApprovalSql, parameters));
        }



        private async Task SaveAttachmentsAsync(int crId, List<IFormFile> files, string uploadedBy)
        {
            if (string.IsNullOrWhiteSpace(uploadedBy))
                throw new ArgumentException("Session expired. Please log in again before uploading attachments.");

            var rootPath = Path.Combine(_attachmentRootFolder, crId.ToString());

            if (!Directory.Exists(rootPath))
                Directory.CreateDirectory(rootPath);

            const string insertAttachmentSql = @"
                INSERT INTO [CRManagementDB].[dbo].[Attachment]
                    (CRID, FileName, FilePath, UploadedBy, UploadedDate, Active)
                VALUES
                    (@CRID, @FileName, @FilePath, @UploadedBy, @UploadedDate, @Active)";

            foreach (var formFile in files)
            {
                if (formFile.Length == 0) continue;

                var safeFileName = Path.GetFileNameWithoutExtension(formFile.FileName);
                var extension = Path.GetExtension(formFile.FileName);
                var uniqueFileName = $"{safeFileName}_{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
                var fullPath = Path.Combine(rootPath, uniqueFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await formFile.CopyToAsync(stream);
                }

                var attachmentParameters = new DynamicParameters();
                attachmentParameters.Add("@CRID", crId);
                attachmentParameters.Add("@FileName", formFile.FileName);
                attachmentParameters.Add("@FilePath", fullPath);
                attachmentParameters.Add("@UploadedBy", uploadedBy);
                attachmentParameters.Add("@UploadedDate", DateTime.Now);
                attachmentParameters.Add("@Active", true);

                _connectionService.ExecuteWithPara(insertAttachmentSql, attachmentParameters);
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

            var targetStatusName = isSubmit ? "Security" : "SecurityDraft";

            var riskAssessment = collection["RiskAssessment"].ToString().Trim();

            int? changeImpactId = int.TryParse(collection["ChangeImpactID"], out var parsedImpactId)
                ? parsedImpactId
                : (int?)null;

            if (isSubmit)
            {
                if (string.IsNullOrWhiteSpace(riskAssessment))
                    throw new ArgumentException("Please fill out the Information Security Risk Assessment.");

                if (changeImpactId == null)
                    throw new ArgumentException("Please select a Change Impact.");
            }

            const string getStatusIdSql = @"
                SELECT TOP 1 StatusID
                FROM [CRManagementDB].[dbo].[CRStatus]
                WHERE StatusName = @StatusName AND Active = 1";

            var statusIdObj = _connectionService.ExecuteScalar(getStatusIdSql, new { StatusName = targetStatusName });

            if (statusIdObj == null || statusIdObj == DBNull.Value)
            {
                throw new InvalidOperationException($"Status '{targetStatusName}' was not found or is inactive in CRStatus table.");
            }

            int targetStatusId = Convert.ToInt32(statusIdObj);

            const string updateCrSql = @"
                UPDATE ChangeRequest
                SET 
                    RiskAssessment = @RiskAssessment,
                    ChangeImpactID = @ChangeImpactID,
                    StatusID       = @StatusID
                WHERE CRID = @CRID
                  AND Active = 1";

            var crParameters = new DynamicParameters();
            crParameters.Add("@RiskAssessment", riskAssessment);
            crParameters.Add("@ChangeImpactID", changeImpactId, DbType.Int32);
            crParameters.Add("@StatusID", targetStatusId);
            crParameters.Add("@CRID", crId);

            int rowsAffected = _connectionService.ExecuteWithPara(updateCrSql, crParameters);

            if (rowsAffected <= 0)
                return false;

            if (isSubmit)
            {
                await UpdateSecurityApprovalStepAsync(crId, empId);
            }

            return true;
        }

        public Task<IEnumerable<ChangeRequest>> GetChangeImpactsAsync()
        {
            const string sql = @"
                SELECT ImpactID   AS ChangeImpactID,
                       ImpactName AS ChangeImpactName
                FROM [CRManagementDB].[dbo].[ChangeImpact]
                WHERE Active = 1
                ORDER BY ImpactID";

            var result = _connectionService.Query<ChangeRequest>(sql, new { })
                            ?? Enumerable.Empty<ChangeRequest>();

            return Task.FromResult(result);
        }

        private async Task UpdateSecurityApprovalStepAsync(int crId, string empId)
        {
            const string getStepIdSql = @"
                SELECT TOP 1 StepID
                FROM [CRManagementDB].[dbo].[WorkflowStep]
                WHERE StepName = @StepName
                  AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getStepIdSql, new { StepName = "Security" });

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step 'Security' was not found or is inactive.");
            }

            int securityStepId = Convert.ToInt32(stepIdObj);

            const string updateApprovalSql = @"
                UPDATE Approval
                SET IsApproved = 1,
                    ApprovalDate = @ApprovalDate
                WHERE CRID = @CRID
                  AND StepID = @StepID
                  AND Active = 1";

            var updateApprovalParameters = new DynamicParameters();
            updateApprovalParameters.Add("@CRID", crId);
            updateApprovalParameters.Add("@StepID", securityStepId);
            updateApprovalParameters.Add("@ApprovalDate", DateTime.Now);

            await Task.Run(() => _connectionService.ExecuteWithPara(updateApprovalSql, updateApprovalParameters));
        }

        public async Task<bool> AssignTesterAsync(int crId, int approverId, string approvedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");
            if (approverId <= 0)
                throw new ArgumentException("Please select a valid user to assign.");

            const string getStatusIdSql = @"
                SELECT TOP 1 StatusID
                FROM [CRManagementDB].[dbo].[CRStatus]
                WHERE StatusName LIKE '%Testing%' AND Active = 1";

            var statusIdObj = _connectionService.ExecuteScalar(getStatusIdSql);

            if (statusIdObj == null || statusIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Status 'Testing' was not found or is inactive in CRStatus table.");
            }

            int approvedStatusId = Convert.ToInt32(statusIdObj);

            // 3. Update ChangeRequest StatusID
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

            // 4. Fetch the Assessment StepID dynamically from WorkflowStep
            const string getAssessmentStepIdSql = @"
                SELECT TOP 1 StepID
                FROM [CRManagementDB].[dbo].[WorkflowStep]
                WHERE StepName LIKE '%Testing%' AND Active = 1
                ORDER BY StepOrder ASC";

            var stepIdObj = _connectionService.ExecuteScalar(getAssessmentStepIdSql);

            if (stepIdObj == null || stepIdObj == DBNull.Value)
            {
                throw new InvalidOperationException("Workflow step for 'Testing' was not found or is inactive.");
            }

            int assessmentStepId = Convert.ToInt32(stepIdObj);

            // 5. Insert new assignment record into Approval table for the Assessment step
            const string insertNextStepSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            var insertParameters = new DynamicParameters();
            insertParameters.Add("@CRID", crId);
            insertParameters.Add("@StepID", assessmentStepId);
            insertParameters.Add("@AssignedBy", approvedByEmpId);
            insertParameters.Add("@AssignedTo", approverId);
            insertParameters.Add("@AssignedDate", DateTime.Now);
            insertParameters.Add("@Active", true);

            int nextStepRowsAffected = _connectionService.ExecuteWithPara(insertNextStepSql, insertParameters);

            return nextStepRowsAffected > 0;
        }



    }
}
