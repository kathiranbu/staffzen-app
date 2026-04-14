using System.Net.Http.Json;
using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class WorkScheduleService
    {
        private readonly IHttpClientFactory _factory;

        public WorkScheduleService(IHttpClientFactory factory) => _factory = factory;

        public async Task<List<WorkScheduleDto>> GetAllAsync()
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.GetAsync("api/WorkSchedules");
                if (!resp.IsSuccessStatusCode) return new();
                var list = await resp.Content.ReadFromJsonAsync<List<WorkScheduleDto>>();
                return list ?? new();
            }
            catch { return new(); }
        }

        public async Task<WorkScheduleDto?> SaveAsync(WorkScheduleDto dto, bool isNew)
        {
            var client = _factory.CreateClient("API");
            HttpResponseMessage resp;
            if (isNew)
            {
                dto.Id = 0; // Let DB assign the real Id
                resp = await client.PostAsJsonAsync("api/WorkSchedules", dto);
            }
            else
                resp = await client.PutAsJsonAsync($"api/WorkSchedules/{dto.Id}", dto);

            if (!resp.IsSuccessStatusCode)
            {
                // Read the error body so the caller can surface a useful message
                var errorBody = await resp.Content.ReadAsStringAsync();
                throw new Exception($"API returned {(int)resp.StatusCode}: {errorBody}");
            }
            return await resp.Content.ReadFromJsonAsync<WorkScheduleDto>();
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.DeleteAsync($"api/WorkSchedules/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }

    public class WorkScheduleDto
    {
        public int    Id                              { get; set; }
        public string Name                            { get; set; } = "New Schedule";
        public bool   IsDefault                       { get; set; }
        public string Arrangement                     { get; set; } = "Fixed";
        public string WorkingDays                     { get; set; } = "Mon,Tue,Wed,Thu,Fri";
        public string DaySlotsJson                    { get; set; } = "{}";
        public bool   IncludeBeforeStart              { get; set; }
        public int    WeeklyHours                     { get; set; }
        public int    WeeklyMinutes                   { get; set; }
        public string SplitAt                         { get; set; } = "00:00";
        public string BreaksJson                      { get; set; } = "[]";
        public string AutoDeductionsJson              { get; set; } = "[]";
        public bool   DailyOvertime                   { get; set; }
        public bool   DailyOvertimeIsTime             { get; set; }
        public int    DailyOvertimeAfterHours         { get; set; } = 8;
        public int    DailyOvertimeAfterMins          { get; set; }
        public double DailyOvertimeMultiplier         { get; set; } = 1.5;
        public bool   DailyDoubleOvertime             { get; set; }
        public int    DailyDoubleOTAfterHours         { get; set; } = 10;
        public int    DailyDoubleOTAfterMins          { get; set; }
        public double DailyDoubleOTMultiplier         { get; set; } = 1.5;
        public bool   WeeklyOvertime                  { get; set; }
        public int    WeeklyOvertimeAfterHours        { get; set; } = 40;
        public int    WeeklyOvertimeAfterMins         { get; set; }
        public double WeeklyOvertimeMultiplier        { get; set; } = 1.5;
        public bool   RestDayOvertime                 { get; set; }
        public double RestDayOvertimeMultiplier       { get; set; } = 1.5;
        public bool   PublicHolidayOvertime           { get; set; }
        public double PublicHolidayOvertimeMultiplier { get; set; } = 1.5;
        public int?   OrganizationId                  { get; set; }

        // ── Time Tracking Policy → Verification ──────────────────────────────
        public bool   RequireFaceVerification         { get; set; } = false;
        public bool   RequireSelfie                   { get; set; } = false;
        public string UnusualBehavior                 { get; set; } = "Blocked";
    }
}
