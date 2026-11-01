using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ILeakChecker
{
    // Checks a multi-line body, including its length; a null result means the body may be written
    LeakRefusal? CheckBody(string body);

    // Checks a single-line value such as a title or label, whose length limits the caller enforces itself
    LeakRefusal? CheckValue(LeakSubject subject, string value);

    // Refuses a reference to a repository that is not public when the text is written to a public repository
    ValueTask<LeakRefusal?> CheckRepositoryReferencesAsync(
        string repository,
        LeakSubject subject,
        string text,
        CancellationToken cancellationToken
    );
}
