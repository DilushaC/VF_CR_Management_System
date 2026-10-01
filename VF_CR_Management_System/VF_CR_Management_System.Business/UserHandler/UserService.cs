using Dapper;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _configuration;

        public UserService(_ConnectionService connectionService, ADAuthentication aDAuthentication, IConfiguration configuration)
        {
            _connectionService = connectionService;
            _aDAuthentication = aDAuthentication;
            _configuration = configuration;
        }
        //public async Task<UserModel> ValidateUserAsync(string username, string password)
        //{
        //    //string allowedProductName = _configuration.GetValue<string>("AllowedProducts:ProductName");
        //    var allowedProductNames = _configuration
        //        .GetSection("AllowedProducts:ProductNames")
        //        .GetChildren()
        //        .Select(c => c.Value)
        //        .Where(v => !string.IsNullOrWhiteSpace(v))
        //        .ToArray();

        //    var response = await _aDAuthentication.AuthenticatewithAD(username, password);
        //    if (!response.Status)
        //        return null;

        //    // 2. Get User
        //    const string userQuery = @"
        //        SELECT U.Id,U.UserName,U.PrimaryBranchId,U.PrimaryDepartmentId,U.IsActive,R.RoleName FROM Users AS U
        //            INNER JOIN UserRoles AS UR on UR.UserId = U.Id
        //            INNER JOIN Roles AS R ON R.Id = UR.RoleId
        //        WHERE U.UserName = @UserName AND U.IsActive = 1";

        //    var userParams = new DynamicParameters();
        //    userParams.Add("@UserName", username);

        //    var userData = _connectionService.ReturnWithPara2(userQuery, userParams);
        //    if (userData == null || userData.Rows.Count == 0)
        //        return null;

        //    var userRow = userData.Rows[0];

        //    var user = new UserModel
        //    {
        //        Id = userRow.Field<int>("Id"),
        //        UserName = userRow.Field<string>("UserName"),
        //        BranchId = userRow.Field<int>("PrimaryBranchId"),
        //        Dep_Id = userRow.Field<int>("PrimaryDepartmentId"),
        //        DisplayName = response.Data.DisplayName,
        //        DisplayDesignation = response.Data.Title,
        //        DisplayDepartment = response.Data.Department,
        //        RoleName = userRow.Field<string>("RoleName"),
        //        IsActive = userRow.Field<bool>("IsActive")
        //    };


        //    // 4. Get MenuItems with PageUrls properly
        //    const string menuQuery = @"
        //                                 SELECT DISTINCT
        //                                         m.Id,
        //                                         m.MenuTitle,
        //                                         m.ParentMenuId,
        //                                         m.PageId,
        //                                         m.IconClass,
        //                                         m.DisplayOrder,
        //                                         m.IsActive,
        //                                         m.ProductId,
        //                                         pd.ProductName,
        //                                         m.MenuCategoryId,
        //                                         c.CategoryName,
        //                                         p.PageUrl
        //                                     FROM MenuItems m
        //                                     LEFT JOIN Pages p
        //                                         ON m.PageId = p.Id
        //                                     LEFT JOIN MenuCategories c
        //                                         ON m.MenuCategoryId = c.Id
        //                                     LEFT JOIN RolePagePermissions rpp
        //                                         ON m.PageId = rpp.PageId
        //                                     LEFT JOIN Products pd
        //                                 	    ON pd.Id = m.ProductId
        //                                     LEFT JOIN UserRoles ur
        //                                         ON rpp.RoleId = ur.RoleId
        //                                     WHERE m.IsActive = 1
        //                                       AND pd.ProductName IN @ProductNames
        //                                       AND (
        //                                             ur.UserId = @UserId
        //                                             OR m.PageId IS NULL
        //                                           )
        //                                     ORDER BY m.DisplayOrder";



        //    var menuParams = new DynamicParameters();
        //    menuParams.Add("@UserId", user.Id);
        //    menuParams.Add("@ProductNames", allowedProductNames);



        //    var menuData = _connectionService.ReturnWithPara2(menuQuery, menuParams);

        //    if (menuData != null && menuData.Rows.Count > 0)
        //    {
        //        user.MenuItems = menuData.AsEnumerable()
        //        .Select(r => new MenuItem
        //        {
        //            Id = r.Field<int>("Id"),
        //            MenuTitle = r.Field<string>("MenuTitle"),
        //            ParentMenuItemId = r.Field<int?>("ParentMenuId"),
        //            PageId = r.Field<int?>("PageId"),
        //            IconClass = r.Field<string?>("IconClass"),
        //            DisplayOrder = r.Field<int>("DisplayOrder"),
        //            IsActive = r.Field<bool>("IsActive"),
        //            ProductId = r.Field<int?>("ProductId"),
        //            ProductName = r.Field<string>("ProductName"),
        //            CategoryId = r.Field<int?>("MenuCategoryId"),
        //            CategoryName = r.Field<string?>("CategoryName"),
        //            PageUrl = r.Field<string?>("PageUrl")
        //        })
        //        .GroupBy(m => m.Id)          // avoid duplicates from role joins
        //                            .Select(g => g.First())
        //        .OrderBy(m => m.DisplayOrder)
        //        .ToList();
        //    }



        //    user.PageUrls = user.MenuItems
        //    .Where(m => !string.IsNullOrWhiteSpace(m.PageUrl))
        //    .Select(m => m.PageUrl!.StartsWith("/") ? m.PageUrl : "/" + m.PageUrl)
        //    .Distinct()
        //    .ToList();

        //    return user;
        //}

        public async Task<UserModel> ValidateUserAsync(string username, string password)
        {
            //string allowedProductName = _configuration.GetValue<string>("AllowedProducts:ProductName");
            var allowedProductNames = _configuration.GetSection("AllowedProducts:ProductNames").Get<string[]>() ?? Array.Empty<string>();

            var response = await _aDAuthentication.AuthenticatewithAD(username, password);
            if (!response.Status)
                return null;

            // 2. Get User
            const string userQuery = @"
                SELECT U.Id,U.UserName,U.PrimaryBranchId,U.PrimaryDepartmentId,U.IsActive,R.RoleName FROM Users AS U
                    INNER JOIN UserRoles AS UR on UR.UserId = U.Id
                    INNER JOIN Roles AS R ON R.Id = UR.RoleId
                WHERE U.UserName = @UserName AND U.IsActive = 1";

            var userParams = new DynamicParameters();
            userParams.Add("@UserName", username);

            var userData = _connectionService.ReturnWithPara2(userQuery, userParams);
            if (userData == null || userData.Rows.Count == 0)
                return null;

            var userRow = userData.Rows[0];

            var user = new UserModel
            {
                Id = userRow.Field<int>("Id"),
                UserName = userRow.Field<string>("UserName"),
                BranchId = userRow.Field<int>("PrimaryBranchId"),
                Dep_Id = userRow.Field<int>("PrimaryDepartmentId"),
                DisplayName = response.Data.DisplayName,
                DisplayDesignation = response.Data.Title,
                DisplayDepartment = response.Data.Department,
                Email = response.Data.Email,
                RoleName = userRow.Field<string>("RoleName"),
                IsActive = userRow.Field<bool>("IsActive")
            };

            
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
                                             pd.ProductName,
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
                                         LEFT JOIN Products pd
                                         	ON pd.Id = m.ProductId
                                         LEFT JOIN UserRoles ur
                                             ON rpp.RoleId = ur.RoleId
                                         WHERE m.IsActive = 1
                                           AND pd.ProductName IN @ProductNames
                                           AND (
                                                 ur.UserId = @UserId
                                                 OR m.PageId IS NULL
                                               )
                                         ORDER BY m.DisplayOrder";



            var menuParams = new DynamicParameters();
            menuParams.Add("@UserId", user.Id);
            menuParams.Add("@ProductNames", allowedProductNames);

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
                    ProductName = r.Field<string>("ProductName"),
                    CategoryId = r.Field<int?>("MenuCategoryId"),
                    CategoryName = r.Field<string?>("CategoryName"),
                    PageUrl = r.Field<string?>("PageUrl")
                })
                .GroupBy(m => m.Id)          // avoid duplicates from role joins                                    
                .Select(g => g.First())
                .OrderBy(m => m.DisplayOrder)
                .ToList();
            }



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
