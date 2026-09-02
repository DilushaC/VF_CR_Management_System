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
            // 1. Authenticate AD
            var response = await _aDAuthentication.AuthenticatewithAD(username, password);
            if (!response.Status)
                return null;

            // 2. Get User
            const string userQuery = @"
                SELECT *
                FROM Users
                WHERE UserName = @UserName AND IsActive = 1";

            var userParams = new DynamicParameters();
            userParams.Add("@UserName", username);

            var userData = _connectionService.ReturnWithPara2(userQuery, userParams);
            if (userData == null || userData.Rows.Count == 0)
                return null;

            var userRow = userData.Rows[0];

            var user = new UserModel
            {
                Id = userRow.Field<int>("Id"),
                DisplayName = response.Data.DisplayName,
                UserName = response.Data.Username,
                DisplayDesignation = response.Data.Title,
                DisplayDepartment = response.Data.Department,
                Email = response.Data.Email,
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
                // Deduplicate by MenuItem Id to avoid duplicates caused by multiple products
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
