namespace Fulcrum.Sample.Api.Identity;

public sealed class AskForNewPhotoHandler(ILogger<AskForNewPhotoHandler> logger) : IOutcomeHandler<IdDocument, IdCheckOutcome>
{
    public Task HandleAsync(IdDocument state, DecisionResult<IdCheckOutcome> result, CancellationToken cancellationToken)
    {
        logger.LogInformation("Asking {Applicant} for a new photo", state.ApplicantId);
        return Task.CompletedTask;
    }
}

public sealed class CropFaceAndStoreHandler(ILogger<CropFaceAndStoreHandler> logger) : IOutcomeHandler<IdDocument, IdCheckOutcome>
{
    public Task HandleAsync(IdDocument state, DecisionResult<IdCheckOutcome> result, CancellationToken cancellationToken)
    {
        var face = result.Answers.Get(IdCheck.FaceQ).Boxes[0];
        logger.LogInformation("Cropping face {Face} from the document of {Applicant} and storing it", face, state.ApplicantId);
        return Task.CompletedTask;
    }
}

public sealed class KycQueueHandler(ILogger<KycQueueHandler> logger) : IOutcomeHandler<IdDocument, IdCheckOutcome>
{
    public Task HandleAsync(IdDocument state, DecisionResult<IdCheckOutcome> result, CancellationToken cancellationToken)
    {
        logger.LogInformation("Queueing the document of {Applicant} for KYC review (rule {Rule})", state.ApplicantId, result.MatchedRule);
        return Task.CompletedTask;
    }
}
