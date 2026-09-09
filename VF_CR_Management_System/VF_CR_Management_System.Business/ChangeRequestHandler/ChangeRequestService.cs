using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.ChangeRequestHandler
{
    public class ChangeRequestService : IChangeRequestService
    {
        private readonly _ConnectionService _connectionService;

        public ChangeRequestService(_ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }
        public async Task<bool> CreateChangeRequestAsync(IFormCollection collection, string userName, string empId)
        {
            // Required fields
            if (!int.TryParse(collection["ChangeTypeID"], out var changeTypeId))
            {
                throw new ArgumentException("Please select a change type.");
            }
            if (!int.TryParse(collection["PriorityID"], out var priorityId))
            {
                throw new ArgumentException("Please select a change priority.");
            }
            var changeTitle = collection["ChangeTitle"].ToString();
            if (string.IsNullOrWhiteSpace(changeTitle))
            {
                throw new ArgumentException("Please provide a Change Title");
            }
            var summary = collection["Summary"].ToString();
            if (string.IsNullOrWhiteSpace(summary))
            {
                throw new ArgumentException("Please provide a change summary and business justification.");
            }
            var title = collection["Title"].ToString();
            if (string.IsNullOrWhiteSpace(summary))
            {
                throw new ArgumentException("Please provide Title for the Change Request");
            }
            var otherChangeType = collection["OtherChangeType"].ToString();
            if (changeTypeId == 5 && string.IsNullOrWhiteSpace(otherChangeType))
            {
                throw new ArgumentException("Please specify the change type.");
            }
            if (!int.TryParse(collection["DivisionID"], out var divisionId))
            {
                throw new ArgumentException("Please select a Division.");
            }
            if (!int.TryParse(collection["ModuleID"], out var moduleId))
            {
                throw new ArgumentException("Please select a Module.");
            }
            if (!int.TryParse(collection["ApproverID"], out var approverId))
            {
                throw new ArgumentException("Please select a Approver.");
            }
            if (!int.TryParse(collection["StatusID"], out var statusId))
            {
                throw new ArgumentException("Missing or invalid status.");
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
                // CRNumber logic:
                // - StatusID == 2 (Submit & Approve) -> generate the next real sequential number.
                // - StatusID == 1 (Save/draft)        -> "Waiting-{unique suffix}" so multiple
                //                                        drafts don't collide on the unique constraint.
                var crNumber = statusId == 2
                    ? GenerateNextCrNumber()
                    : $"Waiting-{Guid.NewGuid():N}".Substring(0, 16);

                var crParameters = new DynamicParameters();
                crParameters.Add("@CRNumber", crNumber);
                crParameters.Add("@RequesterUserName", empId);
                crParameters.Add("@ChangeTitle", changeTitle);
                crParameters.Add("@Summary", summary);
                crParameters.Add("@ChangeTypeID", changeTypeId);
                crParameters.Add("@OtherType", otherChangeType);
                crParameters.Add("@PriorityID", priorityId);
                crParameters.Add("@ModuleID", moduleId);
                crParameters.Add("@DivisionID", divisionId);
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
                    // Collision on the generated/placeholder number — regenerate and retry.
                    continue;
                }
            }

            if (newCrId <= 0)
                return false;

            // 2. Insert Approval step using the real ChangeRequest.Id (int), not CRNumber
            const string approvalSql = @"
                INSERT INTO Approval
                    (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
                VALUES
                    (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            var approvalParameters = new DynamicParameters();
            approvalParameters.Add("@CRID", newCrId);
            approvalParameters.Add("@StepID", 7);
            approvalParameters.Add("@AssignedBy", empId);
            approvalParameters.Add("@AssignedTo", approverId);
            approvalParameters.Add("@AssignedDate", DateTime.Now);
            approvalParameters.Add("@Active", true);

            int approvalRowsAffected = _connectionService.ExecuteWithPara(approvalSql, approvalParameters);

            return approvalRowsAffected > 0;
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
                    ct.ChangeTypeName AS ChangeType,
                    cr.OtherType,
                    cr.PriorityID,
                    p.PriorityName    AS Priority,
                    cr.DivisionID,
                    dv.DivisionName   AS Division,
                    cr.ModuleID,
                    m.ModuleName      AS Module,
                    cr.StatusID,
                    s.StatusName      AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    cr.RequestedDate
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority]   p  ON p.PriorityID   = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Division]   dv ON dv.DivisionID  = cr.DivisionID
                LEFT JOIN [CRManagementDB].[dbo].[Module]     m  ON m.ModuleID    = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus]   s  ON s.StatusID    = cr.StatusID
                WHERE cr.Active = 1
                  AND cr.CRID = @CRID";

            var result = _connectionService.Query<ChangeRequest>(sql, new { CRID = crId });
            return result.FirstOrDefault();
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

        // Updates an existing draft Change Request in place. Only drafts (CRNumber still
        // "Waiting-...") can be edited this way — once a CR has been submitted/approved/
        // rejected it should go through Submit/Approve/Reject instead, not a raw field edit.
        public async Task<bool> UpdateChangeRequestAsync(int crId, IFormCollection collection, string userName, string empId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");

            if (!int.TryParse(collection["ChangeTypeID"], out var changeTypeId))
            {
                throw new ArgumentException("Please select a change type.");
            }
            if (!int.TryParse(collection["PriorityID"], out var priorityId))
            {
                throw new ArgumentException("Please select a change priority.");
            }
            var changeTitle = collection["ChangeTitle"].ToString();
            if (string.IsNullOrWhiteSpace(changeTitle))
            {
                throw new ArgumentException("Please provide a Change Request Title");
            }
            var summary = collection["Summary"].ToString();
            if (string.IsNullOrWhiteSpace(summary))
            {
                throw new ArgumentException("Please provide a change summary and business justification.");
            }
            var otherChangeType = collection["OtherChangeType"].ToString();
            if (changeTypeId == 5 && string.IsNullOrWhiteSpace(otherChangeType))
            {
                throw new ArgumentException("Please specify the change type.");
            }
            if (!int.TryParse(collection["DivisionID"], out var divisionId))
            {
                throw new ArgumentException("Please select a DIvision.");
            }
            if (!int.TryParse(collection["ModuleID"], out var moduleId))
            {
                throw new ArgumentException("Please select a Module.");
            }
            if (!int.TryParse(collection["ApproverID"], out var approverId))
            {
                throw new ArgumentException("Please select a Approver.");
            }
            // StatusID isn't taken from the form here — editing a draft keeps it a draft
            // (StatusID 1). Use Submit to move it out of draft state.
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
            crParameters.Add("@ChangeTypeID", changeTypeId);
            crParameters.Add("@OtherType", otherChangeType);
            crParameters.Add("@PriorityID", priorityId);
            crParameters.Add("@DivisionID", divisionId);
            crParameters.Add("@ModuleID", moduleId);
            crParameters.Add("@StatusID", draftStatusId);
            crParameters.Add("@CRID", crId);

            int crRowsAffected = _connectionService.ExecuteWithPara(updateCrSql, crParameters);

            // 0 rows means the CR wasn't found, wasn't Active, or isn't a draft anymore —
            // nothing to update, so bail out without touching the Approval table.
            if (crRowsAffected <= 0)
                return false;

            // Keep the assigned implementer (StepID 7) in sync with whatever was picked
            // in the Edit form, updating the existing row if one exists, or inserting a
            // fresh one if this draft somehow doesn't have one yet.
            const int assignStepId = 7;

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

        public Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAsync(string empNo, string filter)
        {
            const int assignStepId = 7;

            string filterCondition = filter switch
            {
                "createdByMe" => "AND cr.RequesterUserName = @EmpNo",

                "assignedToMe" => @"AND cr.CRNumber NOT LIKE 'Waiting%'
                    AND EXISTS (
                        SELECT 1
                        FROM [CRManagementDB].[dbo].[Approval] a
                        WHERE a.CRID = cr.CRID
                            AND a.AssignedTo = @EmpNo
                            AND a.StepID = @StepID
                            AND a.Active = 1
                            AND a.Decision IS NULL
                    )",

                            _ => @"AND (
                cr.RequesterUserName = @EmpNo
                OR (
                    cr.CRNumber NOT LIKE 'Waiting%'
                    AND EXISTS (
                        SELECT 1
                        FROM [CRManagementDB].[dbo].[Approval] a
                        WHERE a.CRID = cr.CRID
                            AND a.AssignedTo = @EmpNo
                            AND a.StepID = @StepID
                            AND a.Active = 1
                            AND a.Decision IS NULL
                    )
                )
                )"
                        };

            var sql = $@"
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
                    cr.RequestedDate
                FROM [CRManagementDB].[dbo].[ChangeRequest] AS cr
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
                    {filterCondition}
                ORDER BY
                    cr.RequestedDate DESC,
                    cr.CRID DESC;";

            var result = _connectionService.Query<ChangeRequest>(sql, new { EmpNo = empNo, StepID = assignStepId });
            return Task.FromResult<IEnumerable<ChangeRequest>>(result);
        }
        public async Task<bool> ApproveChangeRequestAsync(int crId, int approverId, string approvedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");
            if (approverId <= 0)
                throw new ArgumentException("Please select an implementer.");

            const int approveStepId = 8;
            const int approvedStatusId = 3;

            // 1. Insert the Approval step record
            const string approvalSql = @"
            INSERT INTO Approval
                (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Active)
            VALUES
                (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Active)";

            var approvalParameters = new DynamicParameters();
            approvalParameters.Add("@CRID", crId);
            approvalParameters.Add("@StepID", approveStepId);
            approvalParameters.Add("@AssignedBy", approvedByEmpId);
            approvalParameters.Add("@AssignedTo", approverId);
            approvalParameters.Add("@AssignedDate", DateTime.Now);
            approvalParameters.Add("@Active", true);

            int approvalRowsAffected = _connectionService.ExecuteWithPara(approvalSql, approvalParameters);

            if (approvalRowsAffected <= 0)
                return false;

            // 2. Update the ChangeRequest status to Approved
            const string updateStatusSql = @"
            UPDATE ChangeRequest
            SET StatusID = @StatusID
            WHERE CRID = @CRID";

            var statusParameters = new DynamicParameters();
            statusParameters.Add("@StatusID", approvedStatusId);
            statusParameters.Add("@CRID", crId);

            int statusRowsAffected = _connectionService.ExecuteWithPara(updateStatusSql, statusParameters);

            return statusRowsAffected > 0;
        }

        public async Task<bool> RejectChangeRequestAsync(int crId, string rejectReason, string rejectedByEmpId)
        {
            if (crId <= 0)
                throw new ArgumentException("Invalid Change Request.");
            if (string.IsNullOrWhiteSpace(rejectReason))
                throw new ArgumentException("Please provide a reason for rejection.");

            const int rejectStepId = 13;
            const int rejectedStatusId = 7;

            const string approvalSql = @"
            INSERT INTO Approval
                (CRID, StepID, AssignedBy, AssignedTo, AssignedDate, Comments, Active)
            VALUES
                (@CRID, @StepID, @AssignedBy, @AssignedTo, @AssignedDate, @Comments, @Active)";

            var approvalParameters = new DynamicParameters();
            approvalParameters.Add("@CRID", crId);
            approvalParameters.Add("@StepID", rejectStepId);
            approvalParameters.Add("@AssignedBy", rejectedByEmpId);
            approvalParameters.Add("@AssignedTo", rejectedByEmpId); // rejecter acts on it themselves
            approvalParameters.Add("@AssignedDate", DateTime.Now);
            approvalParameters.Add("@Comments", rejectReason);
            approvalParameters.Add("@Active", true);

            int approvalRowsAffected = _connectionService.ExecuteWithPara(approvalSql, approvalParameters);

            if (approvalRowsAffected <= 0)
                return false;

            const string updateStatusSql = @"
            UPDATE ChangeRequest
            SET StatusID = @StatusID
            WHERE CRID = @CRID";

            var statusParameters = new DynamicParameters();
            statusParameters.Add("@StatusID", rejectedStatusId);
            statusParameters.Add("@CRID", crId);

            int statusRowsAffected = _connectionService.ExecuteWithPara(updateStatusSql, statusParameters);

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
                    // rowsAffected == 0 means the CR wasn't found or wasn't in "Waiting-" state anymore
                    return rowsAffected > 0;
                }
                catch (Exception ex) when (attempt < maxAttempts && IsDuplicateCrNumberError(ex))
                {
                    // Collision on the generated number — regenerate and retry.
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

    }
}
