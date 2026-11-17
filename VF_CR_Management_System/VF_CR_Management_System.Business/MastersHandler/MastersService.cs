using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.MastersHandler
{
    public class MastersService : IMastersService
    {
        private readonly _ConnectionService _connectionService;

        public MastersService(_ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        public Task<IEnumerable<Vendor>> GetAllVendorsAsync()
        {
            // Active = 1 only: a "deleted" vendor is soft-deleted (Active = 0) and should
            // no longer appear in the Vendor Master list.
            const string sql = @"
                SELECT
                    VendorID,
                    VendorName,
                    Description,
                    Active
                FROM [CRManagementDB].[dbo].[Vendor]
                WHERE Active = 1
                ORDER BY VendorID";

            var result = _connectionService.Query<Vendor>(sql, new { })
                            ?? Enumerable.Empty<Vendor>();

            return Task.FromResult(result);
        }

        public Task<Vendor> GetVendorByIdAsync(int vendorId)
        {
            if (vendorId <= 0)
                throw new ArgumentException("Invalid Vendor.");

            const string sql = @"
                SELECT
                    VendorID,
                    VendorName,
                    Description,
                    Active
                FROM [CRManagementDB].[dbo].[Vendor]
                WHERE VendorID = @VendorID";

            var result = _connectionService.Query<Vendor>(sql, new { VendorID = vendorId });
            var vendor = result?.FirstOrDefault();

            return Task.FromResult(vendor);
        }

        public Task<int> AddVendorAsync(Vendor vendor)
        {
            if (vendor == null)
                throw new ArgumentException("Invalid Vendor.");

            if (string.IsNullOrWhiteSpace(vendor.VendorName))
                throw new ArgumentException("Vendor name is required.");

            const string sql = @"
                DECLARE @ExistingVendorID INT;
                DECLARE @ExistingActive BIT;

                SELECT
                    @ExistingVendorID = VendorID,
                    @ExistingActive = Active
                FROM [CRManagementDB].[dbo].[Vendor]
                WHERE LTRIM(RTRIM(VendorName)) = @VendorName;

                IF @ExistingVendorID IS NOT NULL
                BEGIN
                    IF @ExistingActive = 1
                    BEGIN
                        THROW 50001, 'This vendor already exists.', 1;
                    END
                    ELSE
                    BEGIN
                        UPDATE [CRManagementDB].[dbo].[Vendor]
                        SET Active = 1,
                            Description = @Description
                        WHERE VendorID = @ExistingVendorID;

                        SELECT @ExistingVendorID;
                    END
                END
                ELSE
                BEGIN
                    INSERT INTO [CRManagementDB].[dbo].[Vendor]
                        (VendorName, Description, Active)
                    VALUES
                        (@VendorName, @Description, 1);

                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                END";

            var parameters = new DynamicParameters();

            parameters.Add("@VendorName", vendor.VendorName.Trim());
            parameters.Add("@Description", (vendor.Description ?? string.Empty).Trim());

            var scalarResult = _connectionService.ExecuteScalar(sql, parameters);

            int vendorId = scalarResult != null
                ? Convert.ToInt32(scalarResult)
                : 0;

            return Task.FromResult(vendorId);
        }

        public Task<bool> UpdateVendorAsync(Vendor vendor)
        {
            if (vendor == null || vendor.VendorID <= 0)
                throw new ArgumentException("Invalid Vendor.");

            if (string.IsNullOrWhiteSpace(vendor.VendorName))
                throw new ArgumentException("Vendor name is required.");

            const string sql = @"
                UPDATE [CRManagementDB].[dbo].[Vendor]
                SET VendorName  = @VendorName,
                    Description = @Description,
                    Active      = @Active
                WHERE VendorID  = @VendorID";

            var parameters = new DynamicParameters();
            parameters.Add("@VendorName", vendor.VendorName.Trim());
            parameters.Add("@Description", (vendor.Description ?? string.Empty).Trim());
            parameters.Add("@Active", vendor.Active);
            parameters.Add("@VendorID", vendor.VendorID);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);

            return Task.FromResult(rowsAffected > 0);
        }

        public Task<bool> DeleteVendorAsync(int vendorId)
        {
            if (vendorId <= 0)
                throw new ArgumentException("Invalid Vendor.");

            // Soft delete: Vendor rows may be referenced elsewhere (e.g. ChangeRequest.VendorID),
            // so we deactivate instead of removing the row.
            const string sql = @"
                UPDATE [CRManagementDB].[dbo].[Vendor]
                SET Active = 0
                WHERE VendorID = @VendorID";

            var parameters = new DynamicParameters();
            parameters.Add("@VendorID", vendorId);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);

            return Task.FromResult(rowsAffected > 0);
        }

        // =====================================================================
        // Division
        // =====================================================================

        public Task<IEnumerable<Division>> GetAllDivisionsAsync()
        {
            const string sql = @"
                SELECT
                    Id,
                    DivisionName,
                    Description,
                    Active
                FROM [CRManagementDB].[dbo].[Division]
                ORDER BY Id";

            var result = _connectionService.Query<Division>(sql, new { })
                            ?? Enumerable.Empty<Division>();

            return Task.FromResult(result);
        }

        public Task<Division> GetDivisionByIdAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid Division.");

            const string sql = @"
                SELECT
                    Id,
                    DivisionName,
                    Description,
                    Active
                FROM [CRManagementDB].[dbo].[Division]
                WHERE Id = @Id";

            var result = _connectionService.Query<Division>(sql, new { Id = id });
            var division = result?.FirstOrDefault();

            return Task.FromResult(division);
        }

        public Task<int> AddDivisionAsync(Division division)
        {
            if (division == null)
                throw new ArgumentException("Invalid Division.");

            if (string.IsNullOrWhiteSpace(division.DivisionName))
                throw new ArgumentException("Division name is required.");

            const string sql = @"
                INSERT INTO [CRManagementDB].[dbo].[Division]
                    (DivisionName, Description, Active)
                VALUES
                    (@DivisionName, @Description, @Active);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionName", division.DivisionName.Trim());
            parameters.Add("@Description", (division.Description ?? string.Empty).Trim());
            parameters.Add("@Active", division.Active);

            var scalarResult = _connectionService.ExecuteScalar(sql, parameters);
            var newId = scalarResult != null ? Convert.ToInt32(scalarResult) : 0;

            return Task.FromResult(newId);
        }

        public Task<bool> UpdateDivisionAsync(Division division)
        {
            if (division == null || division.DivisionID <= 0)
                throw new ArgumentException("Invalid Division.");

            if (string.IsNullOrWhiteSpace(division.DivisionName))
                throw new ArgumentException("Division name is required.");

            const string sql = @"
                UPDATE [CRManagementDB].[dbo].[Division]
                SET DivisionName = @DivisionName,
                    Description  = @Description,
                    Active       = @Active
                WHERE Id         = @Id";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionName", division.DivisionName.Trim());
            parameters.Add("@Description", (division.Description ?? string.Empty).Trim());
            parameters.Add("@Active", division.Active);
            parameters.Add("@Id", division.DivisionID);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);

            return Task.FromResult(rowsAffected > 0);
        }

        public Task<bool> DeleteDivisionAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid Division.");

            const string sql = @"
                DELETE FROM [CRManagementDB].[dbo].[Division]
                WHERE Id = @Id";

            var parameters = new DynamicParameters();
            parameters.Add("@Id", id);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);

            return Task.FromResult(rowsAffected > 0);
        }
    }
}
