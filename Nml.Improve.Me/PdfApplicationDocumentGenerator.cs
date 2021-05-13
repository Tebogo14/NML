using Nml.Improve.Me.Dependencies;
using System;
using System.Linq;

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
            var application = DataContext.Applications.FirstOrDefault(app => app.Id == applicationId);

            if (application != null)
            {
                string view;

                if (baseUri.EndsWith("/"))
                    baseUri = baseUri.Substring(baseUri.Length - 1);

                if (application.State == ApplicationState.Pending)
                    view = GenerateFromPathForPending(application, baseUri);
                else if (application.State == ApplicationState.Activated)
                    view = GenerateFromPathForActivated(application, baseUri);
                else if (application.State == ApplicationState.InReview)
                    view = GenerateFromPathForInReview(application, baseUri);
                else
                {
                    _logger.LogWarning(string.Format("The application is in state '{0}' and no valid document can be generated for it.", application.State));
                    return null;
                }

                var pdfOptions = new PdfOptions
                {
                    PageNumbers = PageNumbers.Numeric,
                    HeaderOptions = new HeaderOptions
                    {
                        HeaderRepeat = HeaderRepeat.FirstPageOnly,
                        HeaderHtml = PdfConstants.Header
                    }
                };

                return _pdfGenerator.GenerateFromHtml(view, pdfOptions).ToBytes();
            }
            else
            {
                _logger.LogWarning(
                    string.Format("No application found for id '{0}'", applicationId));
                return null;
            }
        }

        #region GenerateFromPathForPending

        public string GenerateFromPathForPending(Application application, string baseUri)
        {
            string path = _templatePathProvider.Get("PendingApplication");
            var vm = new PendingApplicationViewModel
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = string.Format("{0} {1}", application.Person.FirstName, application.Person.Surname),
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };

            return View_Generator.GenerateFromPath(string.Format("{0}{1}", baseUri, path), vm);
        }

        #endregion GenerateFromPathForPending

        #region GenerateFromPathForActivated

        public string GenerateFromPathForActivated(Application application, string baseUri)
        {
            string path = _templatePathProvider.Get("ActivatedApplication");

            var portfolioFunds = application.Products.SelectMany(p => p.Funds);

            var vm = new ActivatedApplicationViewModel
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = string.Format("{0} {1}", application.Person.FirstName, application.Person.Surname),
                LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                PortfolioFunds = portfolioFunds,
                PortfolioTotalAmount = portfolioFunds.Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
                                                     .Sum(),
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
            return View_Generator.GenerateFromPath(string.Format("{0}{1}", baseUri, path), vm);
        }

        #endregion GenerateFromPathForActivated

        #region GenerateFromPathForInReview

        public string GenerateFromPathForInReview(Application application, string baseUri)
        {
            var templatePath = _templatePathProvider.Get("InReviewApplication");
            var inReviewMessage = string.Format("Your application has been placed in review{0}",
                                application.CurrentReview.Reason switch
                                {
                                    { } reason when reason.Contains("address") =>
                                        " pending outstanding address verification for FICA purposes.",
                                    { } reason when reason.Contains("bank") =>
                                        " pending outstanding bank account verification.",
                                    _ =>
                                        " because of suspicious account behaviour. Please contact support ASAP."
                                });

            var portfolioFunds = application.Products.SelectMany(p => p.Funds);

            var inReviewApplicationViewModel = new InReviewApplicationViewModel
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = string.Format("{0} {1}", application.Person.FirstName, application.Person.Surname),
                LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                PortfolioFunds = portfolioFunds,
                PortfolioTotalAmount = portfolioFunds.Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
                                                     .Sum(),
                InReviewMessage = inReviewMessage,
                InReviewInformation = application.CurrentReview,
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
            return View_Generator.GenerateFromPath(string.Format("{0}{1}", baseUri, templatePath), inReviewApplicationViewModel);
        }

        #endregion GenerateFromPathForInReview
    }
}