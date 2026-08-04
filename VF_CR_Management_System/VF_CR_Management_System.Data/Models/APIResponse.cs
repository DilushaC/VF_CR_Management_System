using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class ApiResponse<T>
    {
        public bool Status { get; set; }

        public string Message { get; set; }

        public string Errors { get; set; }

        public T Data { get; set; }

        public Exception Exception { get; set; }


        public ApiResponse()
        {
            Status = true;
        }
    }
}
