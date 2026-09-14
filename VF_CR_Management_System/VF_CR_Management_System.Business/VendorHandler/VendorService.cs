using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.VendorHandler
{
    public class VendorService : IVendorService
    {
        private readonly _ConnectionService _connectionService; 

        public VendorService(_ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        public async Task<List<Vendor>> GetAllVendorsAsync()
        {
            const string vendorQuery = @"
                SELECT VendorID, VendorName, Active
                FROM Vendor
                WHERE Active = 1
                ORDER BY VendorName";

            var vendorParams = new DynamicParameters();
            var vendorData = _connectionService.ReturnWithPara(vendorQuery, vendorParams);

            if (vendorData == null || vendorData.Rows.Count == 0)
                return new List<Vendor>();

            var vendors = vendorData.AsEnumerable()
                .Select(r => new Vendor
                {
                    Id = r.Field<int>("VendorID"),
                    VendorName = r.Field<string>("VendorName"),
                    Active = r.Field<bool>("Active")
                })
                .ToList();

            return vendors;
        }
    }
}
