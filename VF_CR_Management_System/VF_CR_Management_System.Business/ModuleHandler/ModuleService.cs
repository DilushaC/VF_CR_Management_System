using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ChangeRequestHandler;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.ModuleHandler
{
    public class ModuleService : IModuleService
    {
        private readonly _ConnectionService _connectionService;

        public ModuleService(_ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        public async Task<List<Module>> GetAllModulesAsync()
        {
            const string moduleQuery = @"
                SELECT ModuleID, ModuleName, Active
                FROM Module
                WHERE Active = 1
                ORDER BY ModuleName";

            var moduleParams = new DynamicParameters();
            var moduleData = _connectionService.ReturnWithPara(moduleQuery, moduleParams);

            if (moduleData == null || moduleData.Rows.Count == 0)
                return new List<Module>();

            var modules = moduleData.AsEnumerable()
                .Select(r => new Module
                {
                    Id = r.Field<int>("ModuleID"),
                    ModuleName = r.Field<string>("ModuleName"),
                    IsActive = r.Field<bool>("Active")
                })
                .ToList();

            return modules;
        }
    }
}
