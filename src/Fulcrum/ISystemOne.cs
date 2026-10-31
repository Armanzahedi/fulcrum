namespace Fulcrum;

public interface ISystemOne
{
    Task<Answers> AskAsync(Content state, IReadOnlyList<Question> questions, CancellationToken cancellationToken);
}
