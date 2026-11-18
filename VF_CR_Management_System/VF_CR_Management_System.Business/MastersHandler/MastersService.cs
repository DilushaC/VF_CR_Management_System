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

        // =====================================================================
        // Vendor
        // =====================================================================

        public Task<IEnumerable<Vendor>> GetAllVendorsAsync()
        {
            const string sql = @"
                SELECT VendorID, VendorName, Description, Active
                FROM [CRManagementDB].[dbo].[Vendor]
                WHERE Active = 1
                ORDER BY VendorID DESC";

            var result = _connectionService.Query<Vendor>(sql, new { })
                         ?? Enumerable.Empty<Vendor>();

            return Task.FromResult(result);
        }

        public Task<Vendor> GetVendorByIdAsync(int vendorId)
        {
            if (vendorId <= 0)
                throw new ArgumentException("Invalid Vendor.");

            const string sql = @"
                SELECT VendorID, VendorName, Description, Active
                FROM [CRManagementDB].[dbo].[Vendor]
                WHERE VendorID = @VendorID";

            var result = _connectionService.Query<Vendor>(sql, new { VendorID = vendorId });
            return Task.FromResult(result?.FirstOrDefault());
        }

        public Task<int> AddVendorAsync(Vendor vendor)
        {
            if (vendor == null)
                throw new ArgumentException("Invalid Vendor.");

            if (string.IsNullOrWhiteSpace(vendor.VendorName))
                throw new ArgumentException("Vendor name is required.");

            // Requires a FILTERED unique index on VendorName WHERE Active = 1
            // (replacing the old table-wide unique index) so a deleted vendor's
            // name no longer blocks inserting a new, active vendor with that name.
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Vendor] WITH (UPDLOCK, HOLDLOCK)
                    WHERE LTRIM(RTRIM(VendorName)) = @VendorName
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                INSERT INTO [CRManagementDB].[dbo].[Vendor] (VendorName, Description, Active)
                VALUES (@VendorName, @Description, 1);

                DECLARE @NewVendorID INT = CAST(SCOPE_IDENTITY() AS INT);

                COMMIT TRANSACTION;
                SELECT @NewVendorID;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@VendorName", vendor.VendorName.Trim());
            parameters.Add("@Description", (vendor.Description ?? string.Empty).Trim());

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int vendorId = result != null ? Convert.ToInt32(result) : 0;

            if (vendorId == -1)
                throw new ArgumentException("This vendor already exists.");

            if (vendorId == 0)
                throw new ArgumentException("Unable to save vendor.");

            return Task.FromResult(vendorId);
        }

        public Task<bool> UpdateVendorAsync(Vendor vendor)
        {
            if (vendor == null || vendor.VendorID <= 0)
                throw new ArgumentException("Invalid Vendor.");

            if (string.IsNullOrWhiteSpace(vendor.VendorName))
                throw new ArgumentException("Vendor name is required.");

            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Vendor] WITH (UPDLOCK, HOLDLOCK)
                    WHERE LTRIM(RTRIM(VendorName)) = @VendorName
                      AND Active = 1
                      AND VendorID <> @VendorID
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                UPDATE [CRManagementDB].[dbo].[Vendor]
                SET VendorName = @VendorName,
                    Description = @Description,
                    Active = @Active
                WHERE VendorID = @VendorID;

                DECLARE @RowsAffected INT = @@ROWCOUNT;

                COMMIT TRANSACTION;
                SELECT @RowsAffected;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@VendorName", vendor.VendorName.Trim());
            parameters.Add("@Description", (vendor.Description ?? string.Empty).Trim());
            parameters.Add("@Active", vendor.Active);
            parameters.Add("@VendorID", vendor.VendorID);

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int rowsAffected = result != null ? Convert.ToInt32(result) : 0;

            if (rowsAffected == -1)
                throw new ArgumentException("This vendor already exists.");

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
                SELECT DivisionID, DivisionName, Description, Active
                FROM [CRManagementDB].[dbo].[Division]
                WHERE Active = 1
                ORDER BY DivisionID DESC";

            var result = _connectionService.Query<Division>(sql, new { })
                         ?? Enumerable.Empty<Division>();

            return Task.FromResult(result);
        }

        public Task<Division> GetDivisionByIdAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid Division.");

            const string sql = @"
                SELECT DivisionID, DivisionName, Description, Active
                FROM [CRManagementDB].[dbo].[Division]
                WHERE DivisionID = @DivisionID";

            var result = _connectionService.Query<Division>(sql, new { DivisionID = id });
            return Task.FromResult(result?.FirstOrDefault());
        }

        public Task<int> AddDivisionAsync(Division division)
        {
            if (division == null)
                throw new ArgumentException("Invalid Division.");

            if (string.IsNullOrWhiteSpace(division.DivisionName))
                throw new ArgumentException("Division name is required.");

            // DivisionName has a table-level UNIQUE index across ALL rows, active or not.
            // Deleted (inactive) rows are never reactivated or touched on Add.
            // Division has no table-wide unique index on DivisionName (only the PK on
            // DivisionID), so an active-only duplicate check is all that's needed.
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Division] WITH (UPDLOCK, HOLDLOCK)
                    WHERE LTRIM(RTRIM(DivisionName)) = @DivisionName
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                INSERT INTO [CRManagementDB].[dbo].[Division] (DivisionName, Description, Active)
                VALUES (@DivisionName, @Description, 1);

                DECLARE @NewDivisionID INT = CAST(SCOPE_IDENTITY() AS INT);

                COMMIT TRANSACTION;
                SELECT @NewDivisionID;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionName", division.DivisionName.Trim());
            parameters.Add("@Description", (division.Description ?? string.Empty).Trim());

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int divisionId = result != null ? Convert.ToInt32(result) : 0;

            if (divisionId == -1)
                throw new ArgumentException("This division already exists.");

            if (divisionId == 0)
                throw new ArgumentException("Unable to save division.");

            return Task.FromResult(divisionId);
        }

        public Task<bool> UpdateDivisionAsync(Division division)
        {
            if (division == null || division.DivisionID <= 0)
                throw new ArgumentException("Invalid Division.");

            if (string.IsNullOrWhiteSpace(division.DivisionName))
                throw new ArgumentException("Division name is required.");

            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Division] WITH (UPDLOCK, HOLDLOCK)
                    WHERE LTRIM(RTRIM(DivisionName)) = @DivisionName
                      AND Active = 1
                      AND DivisionID <> @DivisionID
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                UPDATE [CRManagementDB].[dbo].[Division]
                SET DivisionName = @DivisionName,
                    Description = @Description,
                    Active = @Active
                WHERE DivisionID = @DivisionID;

                DECLARE @RowsAffected INT = @@ROWCOUNT;

                COMMIT TRANSACTION;
                SELECT @RowsAffected;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionName", division.DivisionName.Trim());
            parameters.Add("@Description", (division.Description ?? string.Empty).Trim());
            parameters.Add("@Active", division.Active);
            parameters.Add("@DivisionID", division.DivisionID);

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int rowsAffected = result != null ? Convert.ToInt32(result) : 0;

            if (rowsAffected == -1)
                throw new ArgumentException("This division already exists.");

            return Task.FromResult(rowsAffected > 0);
        }

        public Task<bool> DeleteDivisionAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid Division.");

            // Block the soft-delete if any active Module or Workflow still
            // points at this division, so no child row is left orphaned on
            // a deleted parent.
            const string sql = @"
                DECLARE @ModuleCount INT, @WorkflowCount INT;

                SELECT @ModuleCount = COUNT(*)
                FROM [CRManagementDB].[dbo].[Module]
                WHERE DivisionID = @DivisionID
                  AND Active = 1;

                SELECT @WorkflowCount = COUNT(*)
                FROM [CRManagementDB].[dbo].[Workflow]
                WHERE DivisionID = @DivisionID
                  AND Active = 1;

                IF @ModuleCount > 0 OR @WorkflowCount > 0
                BEGIN
                    SELECT -1;
                    RETURN;
                END;

                UPDATE [CRManagementDB].[dbo].[Division]
                SET Active = 0
                WHERE DivisionID = @DivisionID;

                SELECT @@ROWCOUNT;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionID", id);

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int rowsAffected = result != null ? Convert.ToInt32(result) : 0;

            if (rowsAffected == -1)
                throw new ArgumentException("This division is assigned to one or more modules or workflows and cannot be deleted.");

            return Task.FromResult(rowsAffected > 0);
        }

        // =====================================================================
        // Module
        // =====================================================================

        public Task<IEnumerable<Module>> GetAllModulesAsync()
        {
            const string sql = @"
                SELECT
                    M.ModuleID AS Id,
                    M.ModuleName,
                    M.DivisionID,
                    M.Description,
                    M.Active AS IsActive,
                    D.DivisionName
                FROM [CRManagementDB].[dbo].[Module] M
                LEFT JOIN [CRManagementDB].[dbo].[Division] D
                    ON M.DivisionID = D.DivisionID
                WHERE M.Active = 1
                ORDER BY M.ModuleID DESC";

            var result = _connectionService.Query<Module>(sql, new { })
                         ?? Enumerable.Empty<Module>();

            return Task.FromResult(result);
        }

        public Task<int> AddModuleAsync(Module module)
        {
            if (module == null)
                throw new ArgumentException("Invalid Module.");

            if (module.DivisionID <= 0)
                throw new ArgumentException("Please select a division.");

            if (string.IsNullOrWhiteSpace(module.ModuleName))
                throw new ArgumentException("Module name is required.");

            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Division]
                    WHERE DivisionID = @DivisionID
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 0;
                    RETURN;
                END;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Module] WITH (UPDLOCK, HOLDLOCK)
                    WHERE DivisionID = @DivisionID
                      AND LTRIM(RTRIM(ModuleName)) = @ModuleName
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                INSERT INTO [CRManagementDB].[dbo].[Module] (DivisionID, ModuleName, Description, Active)
                VALUES (@DivisionID, @ModuleName, @Description, 1);

                DECLARE @NewModuleID INT = CAST(SCOPE_IDENTITY() AS INT);

                COMMIT TRANSACTION;
                SELECT @NewModuleID;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionID", module.DivisionID);
            parameters.Add("@ModuleName", module.ModuleName.Trim());
            parameters.Add("@Description", (module.Description ?? string.Empty).Trim());

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int moduleId = result != null ? Convert.ToInt32(result) : 0;

            if (moduleId == -1)
                throw new ArgumentException("This module already exists under the selected division.");

            if (moduleId == 0)
                throw new ArgumentException("The selected division is invalid or inactive.");

            return Task.FromResult(moduleId);
        }

        public Task<bool> UpdateModuleAsync(Module module)
        {
            if (module == null || module.Id <= 0)
                throw new ArgumentException("Invalid Module.");

            if (module.DivisionID <= 0)
                throw new ArgumentException("Please select a division.");

            if (string.IsNullOrWhiteSpace(module.ModuleName))
                throw new ArgumentException("Module name is required.");

            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Division]
                    WHERE DivisionID = @DivisionID
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 0;
                    RETURN;
                END;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Module] WITH (UPDLOCK, HOLDLOCK)
                    WHERE DivisionID = @DivisionID
                      AND LTRIM(RTRIM(ModuleName)) = @ModuleName
                      AND Active = 1
                      AND ModuleID <> @ModuleID
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                UPDATE [CRManagementDB].[dbo].[Module]
                SET DivisionID = @DivisionID,
                    ModuleName = @ModuleName,
                    Description = @Description,
                    Active = @Active
                WHERE ModuleID = @ModuleID;

                DECLARE @RowsAffected INT = @@ROWCOUNT;

                COMMIT TRANSACTION;
                SELECT @RowsAffected;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionID", module.DivisionID);
            parameters.Add("@ModuleName", module.ModuleName.Trim());
            parameters.Add("@Description", (module.Description ?? string.Empty).Trim());
            parameters.Add("@Active", module.IsActive);
            parameters.Add("@ModuleID", module.Id);

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int rowsAffected = result != null ? Convert.ToInt32(result) : 0;

            if (rowsAffected == -1)
                throw new ArgumentException("This module already exists under the selected division.");

            if (rowsAffected == 0)
                throw new ArgumentException("Unable to update module. Check the selected division and module.");

            return Task.FromResult(rowsAffected > 0);
        }

        public Task<bool> DeleteModuleAsync(int moduleId)
        {
            if (moduleId <= 0)
                throw new ArgumentException("Invalid Module.");

            const string sql = @"
                UPDATE [CRManagementDB].[dbo].[Module]
                SET Active = 0
                WHERE ModuleID = @ModuleID";

            var parameters = new DynamicParameters();
            parameters.Add("@ModuleID", moduleId);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);
            return Task.FromResult(rowsAffected > 0);
        }

        // =====================================================================
        // Workflow
        // =====================================================================

        public Task<IEnumerable<Workflow>> GetAllWorkflowsAsync()
        {
            const string sql = @"
                SELECT
                    W.WorkflowID,
                    W.DivisionID,
                    W.WorkflowName,
                    W.Description,
                    W.Active,
                    D.DivisionName
                FROM [CRManagementDB].[dbo].[Workflow] W
                LEFT JOIN [CRManagementDB].[dbo].[Division] D
                    ON W.DivisionID = D.DivisionID
                WHERE W.Active = 1
                ORDER BY W.WorkflowID DESC";

            var result = _connectionService.Query<Workflow>(sql, new { })
                         ?? Enumerable.Empty<Workflow>();

            return Task.FromResult(result);
        }

        public Task<int> AddWorkflowAsync(Workflow workflow)
        {
            if (workflow == null)
                throw new ArgumentException("Invalid Workflow.");

            if (workflow.DivisionID <= 0)
                throw new ArgumentException("Please select a division.");

            if (string.IsNullOrWhiteSpace(workflow.WorkflowName))
                throw new ArgumentException("Workflow name is required.");

            // Name uniqueness is scoped per division, checking active rows only.
            // A deleted (inactive) workflow's name never blocks a new, active one.
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Division]
                    WHERE DivisionID = @DivisionID
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 0;
                    RETURN;
                END;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Workflow] WITH (UPDLOCK, HOLDLOCK)
                    WHERE DivisionID = @DivisionID
                      AND LTRIM(RTRIM(WorkflowName)) = @WorkflowName
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                INSERT INTO [CRManagementDB].[dbo].[Workflow] (DivisionID, WorkflowName, Description, Active)
                VALUES (@DivisionID, @WorkflowName, @Description, 1);

                DECLARE @NewWorkflowID INT = CAST(SCOPE_IDENTITY() AS INT);

                COMMIT TRANSACTION;
                SELECT @NewWorkflowID;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionID", workflow.DivisionID);
            parameters.Add("@WorkflowName", workflow.WorkflowName.Trim());
            parameters.Add("@Description", (workflow.Description ?? string.Empty).Trim());

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int workflowId = result != null ? Convert.ToInt32(result) : 0;

            if (workflowId == -1)
                throw new ArgumentException("This workflow already exists under the selected division.");

            if (workflowId == 0)
                throw new ArgumentException("The selected division is invalid or inactive.");

            return Task.FromResult(workflowId);
        }

        public Task<bool> UpdateWorkflowAsync(Workflow workflow)
        {
            if (workflow == null || workflow.WorkflowID <= 0)
                throw new ArgumentException("Invalid Workflow.");

            if (workflow.DivisionID <= 0)
                throw new ArgumentException("Please select a division.");

            if (string.IsNullOrWhiteSpace(workflow.WorkflowName))
                throw new ArgumentException("Workflow name is required.");

            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Division]
                    WHERE DivisionID = @DivisionID
                      AND Active = 1
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 0;
                    RETURN;
                END;

                IF EXISTS (
                    SELECT 1
                    FROM [CRManagementDB].[dbo].[Workflow] WITH (UPDLOCK, HOLDLOCK)
                    WHERE DivisionID = @DivisionID
                      AND LTRIM(RTRIM(WorkflowName)) = @WorkflowName
                      AND Active = 1
                      AND WorkflowID <> @WorkflowID
                )
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT -1;
                    RETURN;
                END;

                UPDATE [CRManagementDB].[dbo].[Workflow]
                SET DivisionID = @DivisionID,
                    WorkflowName = @WorkflowName,
                    Description = @Description,
                    Active = @Active
                WHERE WorkflowID = @WorkflowID;

                DECLARE @RowsAffected INT = @@ROWCOUNT;

                COMMIT TRANSACTION;
                SELECT @RowsAffected;
            ";

            var parameters = new DynamicParameters();
            parameters.Add("@DivisionID", workflow.DivisionID);
            parameters.Add("@WorkflowName", workflow.WorkflowName.Trim());
            parameters.Add("@Description", (workflow.Description ?? string.Empty).Trim());
            parameters.Add("@Active", workflow.Active);
            parameters.Add("@WorkflowID", workflow.WorkflowID);

            var result = _connectionService.ExecuteScalar(sql, parameters);
            int rowsAffected = result != null ? Convert.ToInt32(result) : 0;

            if (rowsAffected == -1)
                throw new ArgumentException("This workflow already exists under the selected division.");

            if (rowsAffected == 0)
                throw new ArgumentException("Unable to update workflow. Check the selected division and workflow.");

            return Task.FromResult(rowsAffected > 0);
        }

        public Task<bool> DeleteWorkflowAsync(int workflowId)
        {
            if (workflowId <= 0)
                throw new ArgumentException("Invalid Workflow.");

            const string sql = @"
                UPDATE [CRManagementDB].[dbo].[Workflow]
                SET Active = 0
                WHERE WorkflowID = @WorkflowID";

            var parameters = new DynamicParameters();
            parameters.Add("@WorkflowID", workflowId);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);
            return Task.FromResult(rowsAffected > 0);
        }
    }
}
