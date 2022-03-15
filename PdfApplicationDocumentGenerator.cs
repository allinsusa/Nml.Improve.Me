using System;
using System.Collections.Generic;
using System.Linq;
using Nml.Improve.Me.Dependencies;

namespace Nml.Improve.Me
{
    public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
    {
        private readonly IDataContext DataContext;
        private IPathProvider _templatePathProvider;
        public IViewGenerator View_Generator;
        internal readonly IConfiguration _configuration;
        private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
        private readonly IPdfGenerator _pdfGenerator;

        public PdfApplicationDocumentGenerator(
            IDataContext dataContext,
            IPathProvider templatePathProvider,
            IViewGenerator viewGenerator,
            IConfiguration configuration,
            IPdfGenerator pdfGenerator,
            ILogger<PdfApplicationDocumentGenerator> logger)
        {
            if (dataContext != null)
                throw new ArgumentNullException(nameof(dataContext));

            DataContext = dataContext;
            _templatePathProvider = templatePathProvider ?? throw new ArgumentNullException("templatePathProvider");
            View_Generator = viewGenerator;
            _configuration = configuration;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pdfGenerator = pdfGenerator;
        }

        public byte[] Generate(Guid applicationId, string baseUri)
        {
            Application application = DataContext.Applications.Single(app => app.Id == applicationId);

            if (application != null)
            {
                if (baseUri.EndsWith("/"))
                {
                    baseUri = baseUri.Substring(baseUri.Length - 1);
                }

                string view;

                switch (application.State)
                {
                    case ApplicationState.Pending:
                        {
                            string path = _templatePathProvider.Get("PendingApplication");
                            var vm = PopulatePendingApplicationInfo(application);
                            view = View_Generator.GenerateFromPath(string.Format("{0}{1}", baseUri, path), vm);
                            break;
                        }

                    case ApplicationState.Activated:
                        {
                            string path = _templatePathProvider.Get("ActivatedApplication");
                            var vm = PopulateActivatedApplicationInfo(application);
                            view = View_Generator.GenerateFromPath(baseUri + path, vm);
                            break;
                        }

                    case ApplicationState.InReview:
                        {
                            var templatePath = _templatePathProvider.Get("InReviewApplication");
                            var inReviewMessage = GetInReviewMessage(application);
                            var inReviewApplicationViewModel = PopulateInReviewApplicationInfo(application, inReviewMessage);
                            view = View_Generator.GenerateFromPath($"{baseUri}{templatePath}", inReviewApplicationViewModel);
                            break;
                        }

                    default:
                        _logger.LogWarning($"The application is in state '{application.State}' and no valid document can be generated for it.");
                        return null;
                }

                var pdfOptions = GeneratePdfOptions();
                var pdf = _pdfGenerator.GenerateFromHtml(view, pdfOptions);
                return pdf.ToBytes();
            }
            else
            {
                _logger.LogWarning($"No application found for id '{applicationId}'");
                return null;
            }
        }

        #region Private Methods
        private InReviewApplicationViewModel PopulateInReviewApplicationInfo(Application application, string inReviewMessage)
        {
            return new InReviewApplicationViewModel
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = string.Format("{0} {1}",
                                    application.Person.FirstName,
                                    application.Person.Surname),
                LegalEntity =
                                    application.IsLegalEntity ? application.LegalEntity : null,
                PortfolioFunds = GetPortfolioFunds(application),
                PortfolioTotalAmount = GetPortfolioTotalAmout(application),
                InReviewMessage = inReviewMessage,
                InReviewInformation = application.CurrentReview,
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
        }
        private ActivatedApplicationViewModel PopulateActivatedApplicationInfo(Application application)
        {
            return new ActivatedApplicationViewModel
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = $"{application.Person.FirstName} {application.Person.Surname}",
                LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                PortfolioFunds = application.Products.SelectMany(p => p.Funds),
                PortfolioTotalAmount = GetPortfolioTotalAmout(application),
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
        }
        private PendingApplicationViewModel PopulatePendingApplicationInfo(Application application)
        {
            return new PendingApplicationViewModel
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = application.Person.FirstName + " " + application.Person.Surname,
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
        }
        private static string GetInReviewMessage(Application application)
        {
            var baseReviewMessage = "Your application has been placed in review";
            var reviewReason = application.CurrentReview.Reason;

            if (reviewReason.Contains("address"))
            {
                return baseReviewMessage += " pending outstanding address verification for FICA purposes.";
            }
            else if (reviewReason.Contains("bank"))
            {
                return baseReviewMessage += " pending outstanding bank account verification.";
            }
            else
            {
                return baseReviewMessage += " because of suspicious account behaviour. Please contact support ASAP.";
            }
        }
        private static IEnumerable<Fund> GetPortfolioFunds(Application application)
        {
            return application.Products.SelectMany(p => p.Funds);
        }
        private static PdfOptions GeneratePdfOptions()
        {
            return new PdfOptions
            {
                PageNumbers = PageNumbers.Numeric,
                HeaderOptions = new HeaderOptions
                {
                    HeaderRepeat = HeaderRepeat.FirstPageOnly,
                    HeaderHtml = PdfConstants.Header
                }
            };
        }
        private double GetPortfolioTotalAmout(Application application)
        {
            return application.Products.SelectMany(p => p.Funds)
                                       .Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
                                       .Sum();
        }
        #endregion
    }
}
