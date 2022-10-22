using Azure.Identity;
using Microsoft.Graph;

namespace AADTools;

// ReSharper disable once UnusedMember.Global
public enum Target
{
    Unknown,
    AppRegistration,
    EnterpriseApp,
    GroupMembers,
    GroupOwners,
}

internal class AADTools
{
    public static async Task Main(string[] args)
    {
        var graph = new GraphServiceClient(new AzureCliCredential());
        User me;

        try
        {
            me = await graph.Me.Request().GetAsync();
        }
        catch (Exception e)
        {
            if (e.InnerException is not CredentialUnavailableException)
                throw;

            Console.WriteLine(e.Message);
            Console.WriteLine(e.InnerException.Message);
            Environment.Exit(1);
            return;
        }

        if (args.Length < 4)
        {
            Console.WriteLine("dotnet run add/sync source_group_or_user target_name [app_registration|enterprise_app|group_members|group_owners]");
            return;
        }

        var action = args[0].ToLower();
        var delete = action == "sync";
        var sourceNameList = args[1];
        var targetName = args[2];
        var displayNameFilter = $"displayName eq '{targetName}'";
        var targetTypeText = args[3];
        var targetType = Enum.GetValues<Target>().FirstOrDefault(val 
            => string.Equals(val.ToString(), targetTypeText.Replace("_", ""), StringComparison.CurrentCultureIgnoreCase));

        var removeNoEmail = args.Any(arg => arg == "--remove-no-email");

        User[] FilterUsers(IEnumerable<DirectoryObject> list)
            => list.Where(m => m is User).Cast<User>().ToArray();

        var sourceMembers = new List<User>();

        foreach (var sourceName in sourceNameList.Split(";"))
        {
            if (sourceName.Contains("@"))
            {
                var user = graph.Users.Request().Filter($"UserPrincipalName eq '{sourceName}'").GetAsync().Result
                    .SingleOrDefault();
                if (user == null)
                {
                    Console.WriteLine($"User {sourceName} not found");
                    Environment.Exit(1);
                    return;
                }
                Console.WriteLine($"Found user {sourceName}, adding");
                sourceMembers.Add(user);
            }
            else
            {
                var sourceGroup = graph.Groups.Request().Filter($"DisplayName eq '{sourceName}'").Expand("members").GetAsync().Result
                    .SingleOrDefault();
                if (sourceGroup == null)
                {
                    Console.WriteLine($"Source group {sourceName} not found");
                    Environment.Exit(1);
                    return;
                }

                Console.WriteLine($"Found group {sourceName}, adding users");
                sourceMembers.AddRange(FilterUsers(sourceGroup.Members));
            }
    
        }


        Console.WriteLine("Users:");
        foreach (var member in sourceMembers)
        {
            Console.WriteLine("  " + member.Mail);
        }

        DirectoryObject? target;
        User[] targetMembers;

        Application appRegistration = null!;
        ServicePrincipal appEnterprise = null!;
        Group targetGroup2 = null!;


        void CheckTarget(DirectoryObject? potentialTarget)
        {
            target = potentialTarget;
            if (target != null)
            {
                Console.WriteLine($"Found {targetType} with name {targetName}, id {target.Id}");
                return;
            }

            Console.WriteLine($"{targetType} with name {targetName} not found.");
            Environment.Exit(1);
        }

        var useMembers = targetType is Target.GroupMembers;
        var targetMemebersName = useMembers ? "members" : "owners";

        switch (targetType)
        {
            case Target.GroupMembers or Target.GroupOwners:
            {
                var owners = targetType == Target.GroupOwners;
                targetGroup2 = graph.Groups.Request().Filter(displayNameFilter).Expand(targetMemebersName).GetAsync().Result.SingleOrDefault()!;
                CheckTarget(targetGroup2);
                targetMembers = owners ? FilterUsers(targetGroup2.Owners) : FilterUsers(targetGroup2.Members);
                break;
            }
            case Target.AppRegistration:
                appRegistration = graph.Applications.Request().Filter(displayNameFilter).Expand("owners").GetAsync().Result.SingleOrDefault()!;
                CheckTarget(appRegistration);
                targetMembers = FilterUsers(appRegistration.Owners);
                break;
            case Target.EnterpriseApp:
                appEnterprise = graph.ServicePrincipals.Request().Filter(displayNameFilter).Expand("owners").GetAsync().Result.SingleOrDefault(sp => sp.ServicePrincipalType != "ManagedIdentity")!;
                CheckTarget(appEnterprise);
                targetMembers = FilterUsers(appEnterprise.Owners);
                break;
            default:
                Console.WriteLine("Unknown target type: " + targetTypeText);
                Environment.Exit(1);
                return;
        }

        async Task DeleteUser(User user)
        {
            Console.WriteLine($"Deleting {user.DisplayName} {user.UserPrincipalName} from {targetType} {targetMemebersName}");
            switch (targetType)
            {
                case Target.AppRegistration:
                    await graph.Applications[appRegistration.Id].Owners[user.Id].Reference.Request().DeleteAsync();
                    break;
                case Target.EnterpriseApp:
                    await graph.ServicePrincipals[appEnterprise.Id].Owners[user.Id].Reference.Request().DeleteAsync();
                    break;
                case Target.GroupMembers or Target.GroupOwners:
                {
                    var req = graph.Groups[targetGroup2.Id];
                    var reference = useMembers ? req.Members[user.Id] : req.Owners[user.Id];
                    await reference.Reference.Request().DeleteAsync();
                    break;
                }
            }
        }

        if (delete)
        {
            foreach (var user in targetMembers)
            {
                if (user.UserPrincipalName == me.UserPrincipalName) continue;

                var shouldBeOwner = sourceMembers.Any(m => m.UserPrincipalName == user.UserPrincipalName);

                if (shouldBeOwner)
                {
                    continue;
                }

                await DeleteUser(user);
            }
        }

        if (removeNoEmail)
        {
            foreach (var user in targetMembers)
            {
                if (user.UserPrincipalName == me.UserPrincipalName) continue;

                var shouldDelete = string.IsNullOrEmpty(user.Mail);

                if (!shouldDelete)
                    continue;

                await DeleteUser(user);
            }
        }

        foreach (var user in sourceMembers)
        {
            var alreadyMember = targetMembers.Any(o => o.Mail == user.Mail);

            if (alreadyMember)
            {
                Console.WriteLine($"{user.Mail} already in {targetMemebersName}");
                continue;
            }

            Console.WriteLine($"Adding {user.Mail} to {targetType} {targetMemebersName}");
            switch (targetType)
            {
                case Target.AppRegistration:
                    await graph.Applications[appRegistration.Id].Owners.References.Request().AddAsync(user);
                    break;
                case Target.EnterpriseApp:
                    await graph.ServicePrincipals[appEnterprise.Id].Owners.References.Request().AddAsync(user);
                    break;
                case Target.GroupMembers or Target.GroupOwners:
                {
                    var req = graph.Groups[targetGroup2.Id];
                    if (useMembers)
                        await req.Members.References.Request().AddAsync(user);
                    else
                        await req.Owners.References.Request().AddAsync(user);

                    break;
                }
            }
        }
    }
}