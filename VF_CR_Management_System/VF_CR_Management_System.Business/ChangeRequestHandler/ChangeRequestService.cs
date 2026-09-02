using Dapper;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ConnectionHandler;

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
            if (!int.TryParse(collection["ApproverID"], out var approverId))
            {
                throw new ArgumentException("Please select a Approver.");
            }

            var crNumber = GenerateNextCrNumber();

            // 1. Insert ChangeRequest, and get back the new identity Id in the same round-trip
            const string crSql = @"
                INSERT INTO ChangeRequest
                    (CRNumber, RequesterUserName, Summary, ChangeTypeID, PriorityID, ModuleID, 
                     RequestedDate, StatusID, Active)
                VALUES
                    (@CRNumber, @RequesterUserName, @Summary, @ChangeTypeID, @PriorityID, @ModuleID,
                     @RequestedDate, @StatusID, @Active);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var crParameters = new DynamicParameters();
            crParameters.Add("@CRNumber", crNumber);
            crParameters.Add("@RequesterUserName", empId);
            crParameters.Add("@Summary", summary);
            crParameters.Add("@ChangeTypeID", changeTypeId);
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
            approvalParameters.Add("@AssignedTo", approverId);
            approvalParameters.Add("@AssignedDate", DateTime.Now);
            approvalParameters.Add("@Active", true);

            int approvalRowsAffected = _connectionService.ExecuteWithPara(approvalSql, approvalParameters);

            return Task.FromResult(approvalRowsAffected > 0);
        }

        private string GenerateNextCrNumber()
        {
            var year = DateTime.Now.Year;

            const string countSql = @"
                SELECT COUNT(1) FROM ChangeRequest
                WHERE YEAR(RequestedDate) = @Year";

            var result = _connectionService.ExecuteScalar(countSql, new { Year = year });
            var countThisYear = result != null ? Convert.ToInt32(result) : 0;

            return $"CR-{year}-{(countThisYear + 1):D6}";
        }
    }
}