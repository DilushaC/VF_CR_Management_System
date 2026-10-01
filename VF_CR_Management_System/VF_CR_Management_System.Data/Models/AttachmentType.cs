using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class AttachmentType
    {
        public int AttachmentTypeID { get; set; }
        public string AttachmentName { get; set; }
        public bool Active { get; set; }
    }
}
