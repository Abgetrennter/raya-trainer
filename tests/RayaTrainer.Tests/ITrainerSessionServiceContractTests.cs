using RayaTrainer.App.Services;
using Xunit;

namespace RayaTrainer.Tests;

/// <summary>
/// 防止 ITrainerSessionService 接口扩展后 TrainerSessionManager 漏实现。
/// 通过反射扫描接口所有成员，确认实现类逐个提供。
/// </summary>
public sealed class ITrainerSessionServiceContractTests
{
    [Fact]
    public void TrainerSessionManagerImplementsEveryInterfaceMember()
    {
        var interfaceType = typeof(ITrainerSessionService);
        var implementorType = typeof(TrainerSessionManager);

        Assert.True(interfaceType.IsAssignableFrom(implementorType),
            $"{implementorType.Name} must implement {interfaceType.Name}.");

        foreach (var method in interfaceType.GetMethods())
        {
            var matching = implementorType.GetMethod(
                method.Name,
                method.GetParameters().Select(p => p.ParameterType).ToArray());
            Assert.True(matching is not null,
                $"{implementorType.Name} is missing interface method {method.Name}.");
        }

        foreach (var property in interfaceType.GetProperties())
        {
            var matching = implementorType.GetProperty(property.Name, property.PropertyType);
            Assert.True(matching is not null,
                $"{implementorType.Name} is missing interface property {property.Name}.");
        }
    }

    [Fact]
    public void InterfaceKeepsSessionStateReadOnlyAndCapabilityDriven()
    {
        var interfaceType = typeof(ITrainerSessionService);

        Assert.Null(interfaceType.GetMethod("SetFeatureController"));
        Assert.Null(interfaceType.GetMethod("SetArePatchesInstalled"));
        Assert.Null(interfaceType.GetMethod("IsFeatureAvailable"));
        Assert.Null(interfaceType.GetMethod("GetFeatureUnavailableReason"));
        Assert.NotNull(interfaceType.GetMethod(nameof(ITrainerSessionService.GetFeatureCapability)));
    }
}
