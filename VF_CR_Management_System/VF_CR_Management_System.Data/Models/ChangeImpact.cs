using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class ChangeImpact
    {
        public int ImpactID { get; set; }
        public string ImpactName { get; set; }
        public bool Active { get; set; }
    }
}
