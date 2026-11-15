using System.Net;
using System.Text;
using Common.Identity;
using Common.Models;
using Web.Monitoring;
namespace Tests;
public class CrewHealthReaderTests
{
    [Fact]
    public async Task RosterKeepsFailuresSeparateFromCriticalReadingsAndUsesLatestOnly()
    {
        var client=new MonitoringClient(new HttpClient(new Handler(request=>
        {
            Assert.Contains("limit=1",request.RequestUri!.Query);
            if(request.RequestUri.AbsolutePath.Contains("654321"))return new(HttpStatusCode.ServiceUnavailable);
            var at=DateTimeOffset.UtcNow.ToString("O");
            var item=request.RequestUri.AbsolutePath.EndsWith("measurements")
                ? "{\"measurement_id\":\"test\",\"measured_at\":\""+at+"\",\"pulse\":{\"bpm\":250,\"quality\":\"good\"},\"photo_url\":null}"
                : "{\"measured_at\":\""+at+"\",\"temperature_c\":22,\"relative_humidity_percent\":45}";
            return new(HttpStatusCode.OK){Content=new StringContent("{\"items\":["+item+"],\"next_cursor\":null}",Encoding.UTF8,"application/json")};
        })){BaseAddress=new("http://local/")});
        var records=await new CrewHealthReader(client).ReadRosterAsync([
            new("first","first","First",null,"Member",AppRoles.Crew,"123456"),
            new("second","second","Second",null,"Member",AppRoles.Medic,"654321")],default);
        Assert.Equal("critical",records["first"].State);
        Assert.True(records["first"].NeedsAttention);
        Assert.Equal("unknown",records["second"].State);
        Assert.NotNull(records["second"].Error);
        Assert.True(records["second"].NeedsAttention);
        Assert.True(records["first"].Priority>records["second"].Priority);
        Assert.Equal("first",records["first"].MemberId);
    }
    [Fact]
    public async Task MissingPinDoesNotContactMonitoring()
    {
        var client=new MonitoringClient(new HttpClient(new Handler(_=>throw new Exception("Unexpected request"))));
        var model=await new CrewHealthReader(client).ReadAsync(new("id","user","Crew",null,"Member",AppRoles.Crew,null),7,true,default);
        Assert.NotNull(model.Error);
        Assert.Equal("unknown",model.State);
        Assert.True(model.NeedsAttention);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(send(request));
    }
}
