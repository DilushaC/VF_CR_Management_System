using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class Division
    {
        public int Id { get; set; }
        public string DivisionName { get; set; }
        public string Description { get; set; }
        public bool Active { get; set; }
    }
}
