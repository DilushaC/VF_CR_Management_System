using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Context
{
    public class DapperContext
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly string _connectionString2;


        public DapperContext(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("Connection");
            _connectionString2 = _configuration.GetConnectionString("Connection2");
        }

        public IDbConnection CreateConnection()
                => new SqlConnection(_connectionString);
        public IDbConnection CreateConnection2()
        => new SqlConnection(_connectionString2);
    }
}
