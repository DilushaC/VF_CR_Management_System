using VF_CR_Management_System.Data.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Business.Authentication
{
    public class ADAuthentication
    {
        private readonly string _url;
        public ADAuthentication(IConfiguration configuration)
        {
            _url = configuration["ADCredentials:URl"];
        }

        public async Task<ApiResponse<ADUserModel>> AuthenticatewithAD(string username, string password)
        {
            var client = new HttpClient();
            var encodedPassword = Uri.EscapeDataString(password);
            var url = $"{_url}?username={username}&password={encodedPassword}";

            try
            {
                var response = await client.PostAsync(url, null); // POST with empty body
                response.EnsureSuccessStatusCode(); // throws if not 2xx

                var json = await response.Content.ReadAsStringAsync();

                // Deserialize JSON into ApiResponse<ADUserModel>
                var result = JsonSerializer.Deserialize<ApiResponse<ADUserModel>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return result;
            }
            catch (HttpRequestException ex)
            {
                // Handle network errors
                return new ApiResponse<ADUserModel>
                {
                    Status = false,
                    Message = ex.Message,
                    Data = null
                };
            }
        }
    }
}
