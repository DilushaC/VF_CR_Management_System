using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VF_CR_Management_System.Data.Context;

namespace VF_CR_Management_System.Business.ConnectionHandler
{
    public class _ConnectionService
    {
        private readonly DapperContext _context;

        public _ConnectionService(DapperContext context)
        {
            _context = context;
        }
        public int InsertAndGetId(string query, DynamicParameters parameters)
        {
            using var connection = _context.CreateConnection();
            return connection.QuerySingle<int>(query, parameters);
        }

        public List<T> Query<T>(string query)
        {
            using var connection = _context.CreateConnection();
            return connection.Query<T>(query).ToList();
        }

        public async Task<DataTable> SingleQueryReturn(string query, int id)
        {
            using var connection = _context.CreateConnection();
            var dataTable = new DataTable();
            // Use ExecuteReaderAsync to get an IDataReader
            using var reader = await connection.ExecuteReaderAsync(query, new { Id = id });
            // Load the IDataReader into the DataTable
            dataTable.Load(reader);
            return dataTable;
        }

        public async Task<DataTable> SingleQueryReturnId(string query, object parameters = null)
        {
            using var connection = _context.CreateConnection();
            var dataTable = new DataTable();

            using var reader = await connection.ExecuteReaderAsync(query, parameters);
            dataTable.Load(reader);

            return dataTable;
        }


        public DataTable Return(string query)
        {
            using var connection = _context.CreateConnection();
            using var reader = connection.ExecuteReader(query, commandTimeout: int.MaxValue);
            var dataTable = new DataTable();
            dataTable.Load(reader);
            return dataTable;
        }

        public DataTable ReturnWithPara(string query, DynamicParameters parameters)
        {
            using var connection = _context.CreateConnection();
            using var reader = connection.ExecuteReader(query, parameters, commandTimeout: int.MaxValue);
            var dataTable = new DataTable();
            dataTable.Load(reader);
            return dataTable;
        }

        public int ExecuteWithPara(string query, DynamicParameters parameters)
        {
            using var connection = _context.CreateConnection();
            // Execute returns number of affected rows
            int rowsAffected = connection.Execute(query, parameters, commandTimeout: int.MaxValue);
            return rowsAffected;
        }


        public object ExecuteScalar(string query, object parameters = null)
        {
            using var connection = _context.CreateConnection();
            var result = connection.ExecuteScalar(query, parameters);
            return result == DBNull.Value ? null : result;
        }


        public DataTable ExecuteQuery(string query)
        {
            using var connection = _context.CreateConnection();
            using var reader = connection.ExecuteReader(query);
            var dataTable = new DataTable();
            dataTable.Load(reader);
            return dataTable;
        }

        public void ExecuteCommand(string query)
        {
            using var connection = _context.CreateConnection();
            connection.Execute(query);
        }

        public DataSet ExecuteMultipleTbl(string query)
        {
            using var connection = _context.CreateConnection();
            var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandType = CommandType.Text;

            var adapter = new SqlDataAdapter((SqlCommand)command);
            var dataSet = new DataSet();
            adapter.Fill(dataSet);
            return dataSet;
        }

        public async Task<T> QueryFirstOrDefaultAsync<T>(string query, object parameters = null)
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<T>(query, parameters);
        }
    }
}
