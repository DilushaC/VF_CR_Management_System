using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System;
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
                    (CRNumber, RequesterUserName, Summary, ChangeTypeID, OtherType, PriorityID, ModuleID, 
                     RequestedDate, StatusID, Active)
                VALUES
                    (@CRNumber, @RequesterUserName, @Summary, @ChangeTypeID, @OtherType, @PriorityID, @ModuleID,
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
                crParameters.Add("@Summary", summary);
                crParameters.Add("@ChangeTypeID", changeTypeId);
                crParameters.Add("@OtherType", otherChangeType);
                crParameters.Add("@PriorityID", priorityId);
                crParameters.Add("@ModuleID", moduleId);
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
                SELECT MAX(CAST(RIGHT(CRNumber, 5) AS INT))
                FROM ChangeRequest
                WHERE CRNumber LIKE @Prefix + '%'";

            var result = _connectionService.ExecuteScalar(maxSql, new { Prefix = prefix });
            var lastNumber = (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;

            return $"{prefix}{(lastNumber + 1):D5}";
        }

        public Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAsync(string empNo, string filter)
        {
            string filterCondition = filter switch
            {
                "createdByMe" => "AND cr.RequesterUserName = @EmpNo",
                "assignedToMe" => @"AND EXISTS (
                                        SELECT 1
                                        FROM [CRManagementDB].[dbo].[Approval] a
                                        WHERE a.CRID = cr.CRID
                                            AND a.AssignedTo = @EmpNo
                                            AND a.Active = 1
                                    )",
                        _ => @"AND (
                            cr.RequesterUserName = @EmpNo
                            OR EXISTS (
                                SELECT 1
                                FROM [CRManagementDB].[dbo].[Approval] a
                                WHERE a.CRID = cr.CRID
                                    AND a.AssignedTo = @EmpNo
                                    AND a.Active = 1
                            )
                        )"
                    };

                var sql = $@"
                SELECT
                    cr.CRID,
                    cr.CRNumber,
                    cr.Summary,
                    ct.ChangeTypeName AS ChangeType,
                    p.PriorityName    AS Priority,
                    m.ModuleName      AS Module,
                    s.StatusName      AS Status,
                    cr.RequesterUserName AS RequestedBy,
                    cr.RequestedDate
                FROM [CRManagementDB].[dbo].[ChangeRequest] cr
                LEFT JOIN [CRManagementDB].[dbo].[ChangeType] ct ON ct.ChangeTypeID = cr.ChangeTypeID
                LEFT JOIN [CRManagementDB].[dbo].[Priority]   p  ON p.PriorityID   = cr.PriorityID
                LEFT JOIN [CRManagementDB].[dbo].[Module]     m  ON m.ModuleID    = cr.ModuleID
                LEFT JOIN [CRManagementDB].[dbo].[CRStatus]   s  ON s.StatusID    = cr.StatusID
                WHERE cr.Active = 1
                  {filterCondition}
                ORDER BY cr.RequestedDate DESC";

            var result = _connectionService.Query<ChangeRequest>(sql, new { EmpNo = empNo });
            return Task.FromResult<IEnumerable<ChangeRequest>>(result);
        }
    }
}