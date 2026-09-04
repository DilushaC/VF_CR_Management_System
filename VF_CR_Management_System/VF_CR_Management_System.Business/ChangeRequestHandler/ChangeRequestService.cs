using Dapper;
using Microsoft.AspNetCore.Http;
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

        public Task<bool> CreateChangeRequestAsync(IFormCollection collection, string userName, string empId)
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
            if (!int.TryParse(collection["ImplementerID"], out var ImplementerId))
            {
                throw new ArgumentException("Please select a Approver.");
            }

            var crNumber = GenerateNextCrNumber();

            // 1. Insert ChangeRequest, and get back the new identity Id in the same round-trip
            const string crSql = @"
                INSERT INTO ChangeRequest
                    (CRNumber, RequesterUserName, Summary, ChangeTypeID, OtherType, PriorityID, ModuleID, 
                     RequestedDate, StatusID, Active)
                VALUES
                    (@CRNumber, @RequesterUserName, @Summary, @ChangeTypeID, @OtherType, @PriorityID, @ModuleID,
                     @RequestedDate, @StatusID, @Active);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var crParameters = new DynamicParameters();
            crParameters.Add("@CRNumber", crNumber);
            crParameters.Add("@RequesterUserName", empId);
            crParameters.Add("@Summary", summary);
            crParameters.Add("@ChangeTypeID", changeTypeId);
            crParameters.Add("@OtherType", otherChangeType);
            crParameters.Add("@PriorityID", priorityId);
            crParameters.Add("@ModuleID", moduleId);
            crParameters.Add("@RequestedDate", DateTime.Now);
            crParameters.Add("@StatusID", 1);
            crParameters.Add("@Active", true);

            // ExecuteScalar (same method used in GenerateNextCrNumber) runs the INSERT
            // then returns the SELECT SCOPE_IDENTITY() result as an object.
            var scalarResult = _connectionService.ExecuteScalar(crSql, crParameters);
            var newCrId = scalarResult != null ? Convert.ToInt32(scalarResult) : 0;

            if (newCrId <= 0)
                return Task.FromResult(false);

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
            approvalParameters.Add("@AssignedTo", ImplementerId);
            approvalParameters.Add("@AssignedDate", DateTime.Now);
            approvalParameters.Add("@Active", true);

            int approvalRowsAffected = _connectionService.ExecuteWithPara(approvalSql, approvalParameters);

            return Task.FromResult(approvalRowsAffected > 0);
        }

        private string GenerateNextCrNumber()
        {
            var now = DateTime.Now;
            var year = now.Year;
            var month = now.Month;

            const string countSql = @"
                SELECT COUNT(1) FROM ChangeRequest
                WHERE YEAR(RequestedDate) = @Year AND MONTH(RequestedDate) = @Month";
            var result = _connectionService.ExecuteScalar(countSql, new { Year = year, Month = month });
            var countThisMonth = result != null ? Convert.ToInt32(result) : 0;

            return $"CR/{year}/{month:D2}/{(countThisMonth + 1):D5}";
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