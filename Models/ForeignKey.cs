using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ExcelToDB_Backend.Models
{
	public class ForeignKey
	{
		public string ForeignKeyColumn { get; set; }
		public string ReferencedTable { get; set; }
		public string ReferencedColumn { get; set; }
	}
}