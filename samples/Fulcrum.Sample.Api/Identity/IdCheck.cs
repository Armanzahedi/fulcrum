namespace Fulcrum.Sample.Api.Identity;

public sealed record IdDocument(string ApplicantId, byte[] Photo, string MediaType, string DeclaredType);
public enum DocumentKind { Passport, NationalId, DrivingLicence, Other }
public enum IdCheckOutcome { Accept, Retake, ManualReview }

public sealed class IdCheck : Decision<IdDocument, IdCheckOutcome>
{
    public static readonly BoxQuestion FaceQ = Question.Box("face",
        "Where is the holder's face photo on the document?");

    public static readonly ChoiceQuestion<DocumentKind> KindQ = Question.Choice<DocumentKind>("kind",
        "What kind of identity document is this?",
        (DocumentKind.Passport,       "Passport data page"),
        (DocumentKind.NationalId,     "National identity card"),
        (DocumentKind.DrivingLicence, "Driving licence"),
        (DocumentKind.Other,          "Anything else"));

    public static readonly YesNoQuestion TamperedQ = Question.YesNo("tampered",
        "Does the document show signs of tampering or editing?");

    protected override Content Describe(IdDocument d) =>
        Content.From($"Applicant declared document type: {d.DeclaredType}")
               .WithImage(d.Photo, d.MediaType);

    protected override void Define(IRules<IdCheckOutcome> r)
    {
        r.When(FaceQ.None() || FaceQ.Count().Above(1)).Then(IdCheckOutcome.Retake, name: "no-single-face");
        r.When(TamperedQ.Above(0.5)).Then(IdCheckOutcome.ManualReview, name: "possible-tampering");
        r.When(KindQ.Is(DocumentKind.Other)).Then(IdCheckOutcome.ManualReview, name: "unknown-document");
        r.WhenAnyConfidenceBelow(0.7).Then(IdCheckOutcome.ManualReview, name: "low-confidence");
        r.Default(IdCheckOutcome.Accept);
        r.OnError(IdCheckOutcome.ManualReview);
    }

    protected override void React(IReactions<IdDocument, IdCheckOutcome> r)
    {
        r.On(IdCheckOutcome.Accept).With<CropFaceAndStoreHandler>();
        r.On(IdCheckOutcome.Retake).With<AskForNewPhotoHandler>();
        r.On(IdCheckOutcome.ManualReview).With<KycQueueHandler>();
    }
}
