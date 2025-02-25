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
        private readonly IDatabaseService _database;
        public DatabaseController(IDatabaseService database)
        {
            _database = database;
        }

        [HttpGet("list")]
        public async Task<IActionResult> Get()
        {
            return Ok(await _database.GetAllDatabases());
        }

        [HttpGet("tables")]
        public async Task<IActionResult> GetTables(string databaseName)
        {
            return Ok(await _database.GetAllTables(databaseName));
        }

        [HttpPost]
        public async Task<IActionResult> PostValues([FromBody] DataRequest values)
        {
            return Ok(await _database.InsertValues(values.DatabaseName, values.TableName, values.Data));
        }
    }

    public class DataRequest
    {
        public string DatabaseName { get; set; }
        public string TableName { get; set; }
        public List<List<string>> Data { get; set; }
    }
}