using System.Reflection;
using NetIndex.Core.Abstractions;

namespace NetIndex.Testing.Common;

public sealed class InterfaceSmokeTests
{
    public static TheoryData<Type> InterfaceTypes =>
        new()
        {
            typeof(INetIndexBuilder),
            typeof(IDocument),
            typeof(IEmbeddingGenerator),
            typeof(IVectorStore),
            typeof(IChatClient),
            typeof(ITenantResolver),
            typeof(IDocumentLoader<>),
            typeof(IChunkingStrategy),
            typeof(IDocumentReranker),
        };

    [Theory]
    [MemberData(nameof(InterfaceTypes))]
    public void Story12_Interfaces_Exist_AndAreInterfaces(Type type)
    {
        Assert.True(type.IsInterface);
    }

    [Theory]
    [MemberData(nameof(InterfaceTypes))]
    public void Story12_AsyncMethods_EndWithAsync_AndCancellationTokenIsLast(Type type)
    {
        foreach (var method in type.GetMethods())
        {
            if (!IsAsyncContract(method))
            {
                continue;
            }

            Assert.EndsWith("Async", method.Name, StringComparison.Ordinal);

            var parameters = method.GetParameters();
            Assert.NotEmpty(parameters);
            Assert.Equal(typeof(CancellationToken), parameters[^1].ParameterType);
        }
    }

    private static bool IsAsyncContract(MethodInfo method)
        => typeof(Task).IsAssignableFrom(method.ReturnType)
           || (method.ReturnType.IsGenericType
               && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
           || (method.ReturnType.IsGenericType
               && method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
}