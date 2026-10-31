using FluentValidation;

namespace AknaTransportApi.Business.Validators.Reports;

public class JobReportQueryValidator<T> : AbstractValidator<T> where T : JobReportQuery
{
    public JobReportQueryValidator()
    {
        RuleFor(x => x).Must(x => x.StartDate is null || x.EndDate is null || x.StartDate <= x.EndDate)
            .WithName("startDate").WithMessage("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        RuleFor(x => x).Must(x => x.StartDate is null || x.EndDate is null || x.EndDate.Value.DayNumber - x.StartDate.Value.DayNumber <= 731)
            .WithName("endDate").WithMessage("Rapor aralığı en fazla 2 yıl olabilir.");
        RuleFor(x => x.Scope).IsInEnum();
    }
}

public sealed class JobReportQueryValidator : JobReportQueryValidator<JobReportQuery>;

public sealed class JobReportPagedQueryValidator : JobReportQueryValidator<JobReportPagedQuery>;
