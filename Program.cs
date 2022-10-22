using Azure.Identity;
using Microsoft.Graph;

var graph = new GraphServiceClient(new AzureCliCredential());

var me = await graph.Me.Request().GetAsync();

if (args.Length < 3)
{
    Console.WriteLine("dotnet run add/sync from_group target_app_or_group [owners]");
    return;
}

var action = args[0].ToLower();
var delete = action == "sync".ToLower();
var groupName = args[1];
var targetName = args[2];
var workOnGroupOwners = args.Length > 3 && args[3] == "owners"; 

User[] FilterUsers(IEnumerable<DirectoryObject> list)
    => list.Where(m => m is User).Cast<User>().Where(m => !string.IsNullOrEmpty(m.Mail)).ToArray();

var group = graph.Groups.Request().Filter($"DisplayName eq '{groupName}'").Expand("members").GetAsync().Result.Single();
var members = FilterUsers(group.Members);
Console.WriteLine("Group members:");
foreach (var member in members)
{
    Console.WriteLine("  " + member.Mail);
}

DirectoryObject? target = null!;
User[] targetMembers = null!;

var app = graph.Applications.Request().Filter($"displayName eq '{targetName}'").Expand("owners").GetAsync().Result.SingleOrDefault();
var targetGroup = graph.Groups.Request().Filter($"DisplayName eq '{targetName}'").Expand(workOnGroupOwners ? "owners" : "members").GetAsync().Result.SingleOrDefault();
if (app != null)
{
    Console.WriteLine($"Found app with name {targetName}");
    target = app;
    targetMembers = FilterUsers(app.Owners);
}
else if (targetGroup != null)
{
    Console.WriteLine($"Found group with name {targetName}");
    target = targetGroup;
    if (workOnGroupOwners)
    {
        Console.WriteLine("Working on group owners, rather than members");
        targetMembers = FilterUsers(targetGroup.Owners);
    }
    else
    {
        targetMembers = FilterUsers(targetGroup.Members);
    }
}
else
{
    Console.WriteLine("Target is not an app or group, exiting.");
}

if (delete)
{
    foreach (var user in targetMembers)
    {
        if (user.Mail == me.Mail) continue;

        var shouldBeOwner = members.Any(m => m.Mail == user.Mail);

        if (!shouldBeOwner)
        {
            Console.WriteLine($"Deleting {user.Mail} from target members");
            if (app != null)
            {
                await graph.Applications[app.Id].Owners[user.Id].Reference.Request().DeleteAsync();
            }
            else if(targetGroup != null)
            {
                var reference = workOnGroupOwners ? graph.Groups[targetGroup.Id].Owners[user.Id] : graph.Groups[targetGroup.Id].Members[user.Id];
                await reference.Reference.Request().DeleteAsync();
            }
        }
    }
}

foreach (var user in members)
{
    var alreadyMember = targetMembers.Any(o => o.Mail == user.Mail);
    
    if (!alreadyMember)
    {
        Console.WriteLine($"Adding {user.Mail} to target members");
        if(app != null)
            await graph.Applications[app.Id].Owners.References.Request().AddAsync(user);
        else if(targetGroup != null)
        {
            if (workOnGroupOwners)
            {
                await graph.Groups[targetGroup.Id].Owners.References.Request().AddAsync(user);
            }
            else
            {
                await graph.Groups[targetGroup.Id].Members.References.Request().AddAsync(user);
            }
        }
    }
    else
    {
        Console.WriteLine($"{user.Mail} already member");
    }
}