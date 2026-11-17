using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class ModuleMasterViewModel
    {
        public IEnumerable<Module> Modules { get; set; }
        public IEnumerable<Division> Divisions { get; set; }
    }
}
