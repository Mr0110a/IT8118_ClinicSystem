namespace ClinicReports.Models
{
    // Matches data from /api/reports/appointment-stats
    public class AppointmentStatsDto
    {
        public int TotalAppointments { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public int Missed { get; set; }
        public int Requested { get; set; }
    }

    // Matches data from /api/reports/doctor-utilisation
    public class DoctorUtilisationDto
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
        public int AvailableSlots { get; set; }
        public int BookedSlots { get; set; }
        public double UtilisationPercentage { get; set; }
    }

    // Matches data from /api/reports/cancellation-rate
    public class CancellationRateDto
    {
        public string Month { get; set; } = string.Empty;
        public int TotalBooked { get; set; }
        public int TotalCancelled { get; set; }
        public double RatePercentage { get; set; }
    }
}