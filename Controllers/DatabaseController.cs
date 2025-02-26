using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExcelToDB_Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExcelToDB_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseController : ControllerBase
    {
        private IDatabaseService _database;
        public DatabaseController(IDatabaseService database)
        {
            _database = database;
        }

        [HttpGet("list")]
        public async Task<IActionResult> Get(string connectionString)
        {
            return Ok(await _database.GetAllDatabases(connectionString));
        }

        [HttpGet("tables")]
        public async Task<IActionResult> GetTables(string connectionString, string databaseName)
        {
            return Ok(await _database.GetAllTables(connectionString, databaseName));
        }

        [HttpPost]
        public async Task<IActionResult> PostValues(DataRequest values)
        {
            return Ok(await _database.InsertValues(values.connectionString, values.DatabaseName, values.TableName, values.Data));
        }
    }

    public class DataRequest
    {
        public string DatabaseName { get; set; }
        public string TableName { get; set; }
        public string connectionString { get; set; }
        public List<List<string>> Data { get; set; }
    }
}