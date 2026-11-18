using System.Collections.Generic;
using System.Threading.Tasks;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.MastersHandler
{
    public interface IMastersService
    {
        Task<IEnumerable<Vendor>> GetAllVendorsAsync();
        Task<Vendor> GetVendorByIdAsync(int vendorId);
        Task<int> AddVendorAsync(Vendor vendor);
        Task<bool> UpdateVendorAsync(Vendor vendor);
        Task<bool> DeleteVendorAsync(int vendorId);

        // ---- Division ----
        Task<IEnumerable<Division>> GetAllDivisionsAsync();
        Task<Division> GetDivisionByIdAsync(int id);
        Task<int> AddDivisionAsync(Division division);
        Task<bool> UpdateDivisionAsync(Division division);
        Task<bool> DeleteDivisionAsync(int id);

        // ---- Modules ----
        Task<IEnumerable<Module>> GetAllModulesAsync();
        Task<int> AddModuleAsync(Module module);
        Task<bool> UpdateModuleAsync(Module module);
        Task<bool> DeleteModuleAsync(int moduleId);

        //---- Workflow --------
        Task<IEnumerable<Workflow>> GetAllWorkflowsAsync();
        Task<int> AddWorkflowAsync(Workflow workflow);
        Task<bool> UpdateWorkflowAsync(Workflow workflow);
        Task<bool> DeleteWorkflowAsync(int workflowId);

        //----WorkflowStep---------
        Task<IEnumerable<WorkflowStep>> GetAllWorkflowStepsAsync();
        Task<int> AddWorkflowStepAsync(WorkflowStep step);
        Task<bool> UpdateWorkflowStepAsync(WorkflowStep step);
        Task<bool> DeleteWorkflowStepAsync(int stepId);
    }
}
