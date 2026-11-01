using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ILeakChecker
{
    // Checks a multi-line body, including its length; a null result means the body may be written
    LeakRefusal? CheckBody(string body);

    // Checks a single-line value such as a title or label, whose length limits the caller enforces itself
    LeakRefusal? CheckValue(LeakSubject subject, string value);
}
