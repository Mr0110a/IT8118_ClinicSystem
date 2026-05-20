using System.ComponentModel.DataAnnotations;
using ClinicAPI.Models.Domain;

namespace ClinicMVC.Models
{
    // ─── Account ──────────────────────────────────────────────────────────────
    public class RegisterViewModel
    {
        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Display(Name = "Confirm Password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required, StringLength(100), Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "CPR (Patients only)")]
        [RegularExpression(@"^\d{9,10}$", ErrorMessage = "CPR must be 9-10 digits.")]
        public string? CPR { get; set; }

        [Display(Name = "Phone")]
        public string? Phone { get; set; }

        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [Required, Display(Name = "Account Type")]
        public string Role { get; set; } = "Patient";
    }

    public class LoginViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember Me")]
        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }

    // ─── Doctor management (Clinic Manager) ───────────────────────────────────
    public class DoctorCreateViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, StringLength(100), Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required, Display(Name = "License Number")]
        public string LicenseNumber { get; set; } = string.Empty;

        public string? Phone { get; set; }

        [Display(Name = "Specializations")]
        public List<int> SpecializationIds { get; set; } = new();
    }

    public class DoctorEditViewModel
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string LicenseNumber { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public List<int> SpecializationIds { get; set; } = new();
    }

    public class ScheduleViewModel
    {
        public int Id { get; set; }
        public int DoctorId { get; set; }

        [Required, Range(0, 6, ErrorMessage = "0 = Sunday, 6 = Saturday")]
        [Display(Name = "Day of Week")]
        public int DayOfWeek { get; set; }

        [Required, DataType(DataType.Time), Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; }

        [Required, DataType(DataType.Time), Display(Name = "End Time")]
        public TimeSpan EndTime { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class LeaveViewModel
    {
        public int Id { get; set; }
        public int DoctorId { get; set; }

        [Required, DataType(DataType.Date), Display(Name = "Start Date")]
        public DateTime StartDate { get; set; }

        [Required, DataType(DataType.Date), Display(Name = "End Date")]
        public DateTime EndDate { get; set; }

        public string? Reason { get; set; }
    }

    // ─── Appointment booking ──────────────────────────────────────────────────
    public class BookAppointmentViewModel
    {
        [Required, Display(Name = "Specialization")]
        public int SpecializationId { get; set; }

        [Required, Display(Name = "Doctor")]
        public int DoctorId { get; set; }

        [Required, DataType(DataType.DateTime), Display(Name = "Start Time")]
        public DateTime DateTimeStart { get; set; } = DateTime.Now.AddDays(1);

        [Required, DataType(DataType.DateTime), Display(Name = "End Time")]
        public DateTime DateTimeEnd { get; set; } = DateTime.Now.AddDays(1).AddMinutes(30);

        public int? PatientId { get; set; } // set by receptionist; ignored for patient self-book

        [StringLength(500)]
        public string? Notes { get; set; }
    }

    // ─── Visit + Prescription recording ───────────────────────────────────────
    public class VisitRecordViewModel
    {
        public int AppointmentId { get; set; }
        public int? VisitRecordId { get; set; }

        public string PatientName { get; set; } = string.Empty;
        public string DoctorName  { get; set; } = string.Empty;
        public DateTime AppointmentDate { get; set; }

        [StringLength(500)]
        public string? Diagnosis { get; set; }

        [StringLength(1000)]
        public string? Treatment { get; set; }

        [StringLength(2000), Display(Name = "Doctor Notes")]
        public string? DoctorNotes { get; set; }

        public List<PrescriptionLineViewModel> Prescriptions { get; set; } = new();
    }

    public class PrescriptionLineViewModel
    {
        public int Id { get; set; }

        [Required, StringLength(150), Display(Name = "Medication")]
        public string MedicationName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Dosage { get; set; }

        [StringLength(100)]
        public string? Frequency { get; set; }

        [StringLength(100)]
        public string? Duration { get; set; }

        [StringLength(500)]
        public string? Instructions { get; set; }
    }

    // ─── Public tracking lookup ───────────────────────────────────────────────
    public class TrackingLookupViewModel
    {
        [Required, Display(Name = "CPR Number")]
        public string CPR { get; set; } = string.Empty;

        [Required, Display(Name = "Reference Code")]
        public string ReferenceCode { get; set; } = string.Empty;
    }

    public class TrackingResultViewModel
    {
        public string? PatientName { get; set; }
        public List<TrackingAppointment> Appointments { get; set; } = new();
        public TrackingVisit? LastVisit { get; set; }
        public bool Found { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class TrackingAppointment
    {
        public int Id { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? DoctorName { get; set; }
        public string? Status { get; set; }
        public string? ReferenceCode { get; set; }
    }

    public class TrackingVisit
    {
        public string? Diagnosis { get; set; }
        public string? Treatment { get; set; }
        public DateTime VisitDate { get; set; }
    }

    // ─── User management (Clinic Manager) ─────────────────────────────────────
    public class UserListViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? CPR { get; set; }
        public string Role { get; set; } = string.Empty;
    }

    public class EditUserRoleViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CurrentRole { get; set; } = string.Empty;

        [Required, Display(Name = "New Role")]
        public string NewRole { get; set; } = string.Empty;
    }

    // ─── Error ────────────────────────────────────────────────────────────────
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
