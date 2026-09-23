using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class Testing
    {
        public int TestID { get; set; }
        public int CRID { get; set; }
        public int TestCycleNumber { get; set; }
        public string TestResult { get; set; }
        public string UATComment { get; set; }
        public DateTime TestingDate { get; set; }
        public bool Active { get; set; }
    }
}
