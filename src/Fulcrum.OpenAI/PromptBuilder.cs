using System.Text;

namespace Fulcrum.OpenAI;

internal static class PromptBuilder
{
    public static string System(IReadOnlyList<Question> questions)
    {
        var sb = new StringBuilder()
            .AppendLine("Answer each question below about the state the user provides.")
            .AppendLine("Reply with JSON that follows the response schema exactly.")
            .AppendLine("Probabilities must be calibrated: each is your honest estimate in [0, 1] that the option is correct.")
            .AppendLine()
            .AppendLine("Questions:");
        foreach (var q in questions)
        {
            sb.AppendLine().Append(q.Name).Append(": ").AppendLine(q.Text);
            switch (q.Kind)
            {
                case QuestionKind.Choice:
                    sb.AppendLine("Pick exactly one option as `choice` and give a probability for every option.");
                    foreach (var (name, description) in q.Choices)
                        sb.Append("  - ").Append(name).Append(": ").AppendLine(description);
                    break;
                case QuestionKind.Score:
                    sb.AppendLine("Pick one level by its 0-based index as `level` and give `probabilities` for every level, in this order.");
                    for (var i = 0; i < q.Choices.Count; i++)
                        sb.Append("  ").Append(i).Append(". ").Append(q.Choices[i].Name).Append(": ").AppendLine(q.Choices[i].Description);
                    break;
                case QuestionKind.YesNo:
                    sb.AppendLine("Give `probability`, the probability that the answer is yes.");
                    break;
                case QuestionKind.Box:
                    sb.AppendLine("Return zero or more `boxes`. Coordinates are normalised to [0,1] of the image width and height, origin top-left; `image` is the 0-based index of the image.");
                    break;
            }
        }
        return sb.ToString();
    }
}
