using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

// Colour is six hexadecimal digits without the leading '#', as the GitHub labels API takes it
[DebuggerDisplay("{Name}: {Colour}")]
public sealed record StandardLabel(string Name, string Colour, string Description);
