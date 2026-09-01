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

            // "Other" (ChangeTypeID == 5) requires the free-text description
            var otherChangeType = collection["OtherChangeType"].ToString();
            if (changeTypeId == 5 && string.IsNullOrWhiteSpace(otherChangeType))
            {
                throw new ArgumentException("Please specify the change type.");
            }

            var crNumber = GenerateNextCrNumber();

            const string sql = @"
                INSERT INTO ChangeRequest
                    (CRNumber, UserName,EmpID, Summary, ChangeTypeID, PriorityID,
                     RequestedDate, StatusID, Active)
                VALUES
                    (@CRNumber, @UserName, @EmpID, @Summary, @ChangeTypeID, @PriorityID,
                     @RequestedDate, @StatusID, @Active)";

            var parameters = new DynamicParameters();
            parameters.Add("@CRNumber", crNumber);
            parameters.Add("@UserName", userName);
            parameters.Add("@EmpID", empId);
            parameters.Add("@Summary", summary);
            parameters.Add("@ChangeTypeID", changeTypeId);
            parameters.Add("@PriorityID", priorityId);
            parameters.Add("@RequestedDate", DateTime.Now);
            parameters.Add("@StatusID", 1); 
            parameters.Add("@Active", true);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);

            return Task.FromResult(rowsAffected > 0);
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
