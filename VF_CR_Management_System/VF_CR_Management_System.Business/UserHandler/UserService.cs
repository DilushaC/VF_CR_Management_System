using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.Authentication;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.UserHandler
{
    public class UserService : IUserService
    {
        private readonly _ConnectionService _connectionService;
        private readonly ADAuthentication _aDAuthentication;

        public UserService(_ConnectionService connectionService, ADAuthentication aDAuthentication)
        {
            _connectionService = connectionService;
            _aDAuthentication = aDAuthentication;
        }

        public async Task<UserModel?> ValidateUserAsync(string username, string password, int productId)
        {
            // 1. Authenticate AD (credential check only — display data now comes from our own tables)
            var response = await _aDAuthentication.AuthenticatewithAD(username, password);
            if (!response.Status)
                return null;

            // 2. Get User joined with Department and Designation
            const string userQuery = @"
                SELECT
                    u.Id,
                    u.UserName,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Phone,
                    u.PrimaryBranchId,
                    u.PrimaryDepartmentId,
                    u.DesignationId,
                    u.IsActive,
                    d.DepartmentName,
                    des.DesignationName
                FROM Users u
                LEFT JOIN Department d
                    ON u.PrimaryDepartmentId = d.Id AND d.IsActive = 1
                LEFT JOIN Designation des
                    ON u.DesignationId = des.Id AND des.IsActive = 1
                WHERE u.UserName = @UserName AND u.IsActive = 1";

            var userParams = new DynamicParameters();
            userParams.Add("@UserName", username);

            var userData = _connectionService.ReturnWithPara2(userQuery, userParams);
            if (userData == null || userData.Rows.Count == 0)
                return null;

            var userRow = userData.Rows[0];

            var firstName = userRow.Field<string?>("FirstName") ?? string.Empty;
            var lastName = userRow.Field<string?>("LastName") ?? string.Empty;

            var user = new UserModel
            {
                Id = userRow.Field<int>("Id"),
                DisplayName = $"{firstName} {lastName}".Trim(),
                UserName = userRow.Field<string>("UserName"),
                DisplayDesignation = userRow.Field<string?>("DesignationName") ?? string.Empty,
                DisplayDepartment = userRow.Field<string?>("DepartmentName") ?? string.Empty,
                Email = userRow.Field<string?>("Email") ?? string.Empty,
                IsActive = userRow.Field<bool>("IsActive")
            };

            // 3. Get ProductIds
            const string productQuery = @"
                SELECT ProductId
                FROM UserProducts
                WHERE UserId = @UserId";

            var productParams = new DynamicParameters();
            productParams.Add("@UserId", user.Id);

            var productData = _connectionService.ReturnWithPara2(productQuery, productParams);
            if (productData != null && productData.Rows.Count > 0)
            {
                user.ProductIds = productData
                    .AsEnumerable()
                    .Select(r => r.Field<int>("ProductId"))
                    .Distinct()
                    .ToList();
            }

            if (!user.ProductIds.Any())
                return user;

            // 4. Get MenuItems with PageUrls properly
            const string menuQuery = @"
                SELECT DISTINCT
                    m.Id,
                    m.MenuTitle,
                    m.ParentMenuId,
                    m.PageId,
                    m.IconClass,
                    m.DisplayOrder,
                    m.IsActive,
                    m.ProductId,
                    m.MenuCategoryId,
                    c.CategoryName,
                    p.PageUrl
                FROM MenuItems m
                LEFT JOIN Pages p 
                    ON m.PageId = p.Id
                LEFT JOIN MenuCategories c 
                    ON m.MenuCategoryId = c.Id
                LEFT JOIN RolePagePermissions rpp
                    ON m.PageId = rpp.PageId
                LEFT JOIN UserRoles ur
                    ON rpp.RoleId = ur.RoleId
                WHERE m.IsActive = 1
                  AND m.ProductId = @ProductId
                  AND (
                        ur.UserId = @UserId
                        OR m.PageId IS NULL
                      )
                ORDER BY m.DisplayOrder";

            var menuParams = new DynamicParameters();
            menuParams.Add("@UserId", user.Id);
            menuParams.Add("@ProductId", productId);

            var menuData = _connectionService.ReturnWithPara2(menuQuery, menuParams);

            if (menuData != null && menuData.Rows.Count > 0)
            {
                user.MenuItems = menuData.AsEnumerable()
                    .Select(r => new MenuItem
                    {
                        Id = r.Field<int>("Id"),
                        MenuTitle = r.Field<string>("MenuTitle"),
                        ParentMenuItemId = r.Field<int?>("ParentMenuId"),
                        PageId = r.Field<int?>("PageId"),
                        IconClass = r.Field<string?>("IconClass"),
                        DisplayOrder = r.Field<int>("DisplayOrder"),
                        IsActive = r.Field<bool>("IsActive"),
                        ProductId = r.Field<int?>("ProductId"),
                        CategoryId = r.Field<int?>("MenuCategoryId"),
                        CategoryName = r.Field<string?>("CategoryName"),
                        PageUrl = r.Field<string?>("PageUrl")
                    })
                    .GroupBy(m => m.Id)
                    .Select(g => g.First())
                    .OrderBy(m => m.DisplayOrder)
                    .ToList();
            }

            // 5. Populate PageUrls for session
            user.PageUrls = user.MenuItems
                .Where(m => !string.IsNullOrWhiteSpace(m.PageUrl))
                .Select(m => m.PageUrl!.StartsWith("/") ? m.PageUrl : "/" + m.PageUrl)
                .Distinct()
                .ToList();

            return user;
        }

        public async Task<List<UserModel>> GetAllUsersAsync()
        {
            const string usersQuery = @"
                SELECT Id, UserName, FirstName, LastName, IsActive
                FROM Users
                WHERE IsActive = 1
                ORDER BY UserName";

            var userParams = new DynamicParameters();
            var userData = _connectionService.ReturnWithPara2(usersQuery, userParams);

            if (userData == null || userData.Rows.Count == 0)
                return new List<UserModel>();

            var users = userData.AsEnumerable()
                .Select(r => new UserModel
                {
                    Id = r.Field<int>("Id"),
                    UserName = r.Field<string>("UserName"),
                    FirstName = r.Field<string>("FirstName"),
                    LastName = r.Field<string>("LastName"),
                    IsActive = r.Field<bool>("IsActive")
                })
                .ToList();

            return users;
        }

    }
}
