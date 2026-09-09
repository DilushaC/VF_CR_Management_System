using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Business.ModuleHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.DivisionHandler
{

    public class DivisionService : IDivisionService
    {
        private readonly _ConnectionService _connectionService;

        public DivisionService(_ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        public async Task<List<Division>> GetAllDivisionsAsync()
        {
            const string moduleQuery = @"
                SELECT DivisionID, DivisionName, Active
                FROM Division
                WHERE Active = 1
                ORDER BY DivisionName";

            var divisionParams = new DynamicParameters();
            var divisionData = _connectionService.ReturnWithPara(moduleQuery, divisionParams);

            if (divisionData == null || divisionData.Rows.Count == 0)
                return new List<Division>();

            var divisions = divisionData.AsEnumerable()
                .Select(r => new Division
                {
                    Id = r.Field<int>("DivisionID"),
                    DivisionName = r.Field<string>("DivisionName"),
                    Active = r.Field<bool>("Active")
                })
                .ToList();

            return divisions;
        }
    }
}
