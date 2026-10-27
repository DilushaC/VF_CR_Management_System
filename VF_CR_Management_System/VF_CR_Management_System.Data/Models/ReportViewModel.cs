using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class ReportViewModel
    {
        public ChangeRequest ChangeRequest { get; set; } = new ChangeRequest();

        public Testing Testing { get; set; } = new Testing();
    }
}
