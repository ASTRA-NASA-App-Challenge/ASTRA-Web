using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Web.Areas.Identity.Pages.Account;
namespace Tests;
public class DemoLoginTests
{
    [Fact]
    public async Task QuickLoginIsUnavailableWhenDemoModeIsDisabled()
    {
        var page=new LoginModel(null!,null!,new ConfigurationBuilder().Build());
        Assert.False(page.DemoEnabled);
        Assert.IsType<NotFoundResult>(await page.OnPostDemoAsync("ground"));
    }
    [Fact]
    public async Task QuickLoginAcceptsOnlyWhitelistedProfiles()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Demo:Enabled"]="true" }).Build();
        var page=new LoginModel(null!,null!,config);
        Assert.IsType<BadRequestResult>(await page.OnPostDemoAsync("administrator"));
    }
}
