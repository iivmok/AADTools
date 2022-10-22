using Azure.Identity;
using Microsoft.Graph;

var graph = new GraphServiceClient(new AzureCliCredential());

var me = await graph.Me.Request().GetAsync();

if (args.Length < 3)
{
    Console.WriteLine("dotnet run add/addAndDelete SOME_GROUP app-name");
    return;
}

var action = args[0].ToLower();
var delete = action == "addAndDelete".ToLower();
var groupName = args[1];
var appName = args[2];

User[] FilterUsers(IEnumerable<DirectoryObject> list)
    => list.Where(m => m is User).Cast<User>().ToArray();

var group = graph.Groups.Request().Filter($"DisplayName eq '{groupName}'").Expand("members").GetAsync().Result.Single();
var members = FilterUsers(group.Members);

var app = graph.Applications.Request().Filter($"displayName eq '{appName}'").Expand("owners").GetAsync().Result.Single();
var owners = FilterUsers(app.Owners.Where(m => m is User));

if(delete)
    foreach (var user in owners)
    {
        if(user.Mail == me.Mail) continue;

        var shouldBeOwner = members.Any(m => m.Mail == user.Mail);
        
        if (!shouldBeOwner)
        {
            Console.WriteLine($"Deleting {user.Mail} from app owners");
            await graph.Applications[app.Id].Owners[user.Id].Reference.Request().DeleteAsync();
        }
    }

foreach (var user in members)
{
    var alreadyOwner = owners.Any(o => o.Mail == user.Mail);
    
    if (!alreadyOwner)
    {
        Console.WriteLine($"Adding {user.Mail} to app owners");
        await graph.Applications[app.Id].Owners.References.Request().AddAsync(user);
    }
}