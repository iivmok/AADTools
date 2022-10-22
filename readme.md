# AADTools

Easily manipulate Azure AD objects from command line

## Usage

`dotnet run command source target target_type`

`command`
- `add` add only
- `sync` add and remove

`source`
- username `user@domain`
- group name `some_group`
- multiple of the above `user@domain;some_group`

`target`
- target name literal `some_group_or_app`

`target_type`
- `AppRegistration` (owners)
- `EnterpriseApp` (owners)
- `GroupMembers`
- `GroupOwners`

Examples:
- `dotnet run sync some_group some_app AppRegistration`
- `dotnet run add user@domain some_group GroupMembers`