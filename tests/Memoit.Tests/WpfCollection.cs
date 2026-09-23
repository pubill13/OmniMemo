using Xunit;

namespace Memoit.Tests;

// WPF's process-wide BAML/theme caches are shared. Match the app's single UI-thread startup
// instead of racing XAML loads from several test classes on unrelated STA threads.
[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfCollection { }
