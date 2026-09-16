using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class Attachment
    {
        public int AttachmentID { get; set; }
        public int CRID { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string UploadedBy { get; set; }
        public DateTime UploadedDate { get; set; }
        public bool Active { get; set; }
    }
}
