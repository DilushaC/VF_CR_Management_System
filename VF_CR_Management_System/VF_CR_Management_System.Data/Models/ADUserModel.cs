using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class ADUserModel
    {
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }

        public string Department { get; set; }
        public string OfficeLocation { get; set; }
        public string TelephoneNumber { get; set; }
        public string Title { get; set; }
    }
}
