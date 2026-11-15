--[[
	TEAM-ONLY DOOR (VIP door)
	Players on the chosen team walk right through it. Everyone else bumps into it
	(or gets killed, if you turn that on).
	It uses Collision Groups, so ONLY team members can pass - the door never "opens"
	for others to sneak through.

	HOW TO USE:
	1. Make teams: in the Explorer, find "Teams", insert a Team, and name it (for example VIP).
	2. Make a door Part (Anchored).
	3. Insert a Script inside the door and paste this in.
	4. Set TEAM_NAME below to your team's exact name.
]]

local Players = game:GetService("Players")
local PhysicsService = game:GetService("PhysicsService")

local door = script.Parent

-- SETTINGS
local TEAM_NAME = "VIP" -- must match the Team's name exactly
local KILL_OTHERS = false -- true = people NOT on the team die if they touch the door
local DOOR_TRANSPARENCY = 0.4

door.Anchored = true
door.Transparency = DOOR_TRANSPARENCY

-- Collision groups: "team members" don't collide with "this team's doors"
local DOOR_GROUP = "TeamDoor_" .. TEAM_NAME
local MEMBER_GROUP = "TeamMember_" .. TEAM_NAME

if not PhysicsService:IsCollisionGroupRegistered(DOOR_GROUP) then
	PhysicsService:RegisterCollisionGroup(DOOR_GROUP)
end
if not PhysicsService:IsCollisionGroupRegistered(MEMBER_GROUP) then
	PhysicsService:RegisterCollisionGroup(MEMBER_GROUP)
end
PhysicsService:CollisionGroupSetCollidable(DOOR_GROUP, MEMBER_GROUP, false)

door.CollisionGroup = DOOR_GROUP

local function isOnTeam(player)
	return player.Team ~= nil and player.Team.Name == TEAM_NAME
end

-- Put every part of a character in the right group
local function setPartGroup(part, onTeam)
	if not part:IsA("BasePart") then
		return
	end
	if onTeam then
		part.CollisionGroup = MEMBER_GROUP
	elseif part.CollisionGroup == MEMBER_GROUP then
		part.CollisionGroup = "Default" -- only undo OUR group, leave others alone
	end
end

local function updateCharacter(player)
	local character = player.Character
	if not character then
		return
	end
	local onTeam = isOnTeam(player)
	for _, part in character:GetDescendants() do
		setPartGroup(part, onTeam)
	end
end

local function onPlayerAdded(player)
	player.CharacterAdded:Connect(function(character)
		updateCharacter(player)
		-- Hats and tools added later need the group too
		character.DescendantAdded:Connect(function(part)
			setPartGroup(part, isOnTeam(player))
		end)
	end)
	player:GetPropertyChangedSignal("Team"):Connect(function()
		updateCharacter(player)
	end)
	updateCharacter(player)
end

Players.PlayerAdded:Connect(onPlayerAdded)
for _, player in Players:GetPlayers() do
	onPlayerAdded(player)
end

-- Optional: zap people who are not on the team
if KILL_OTHERS then
	door.Touched:Connect(function(hit)
		local character = hit:FindFirstAncestorOfClass("Model")
		local player = character and Players:GetPlayerFromCharacter(character)
		if player and not isOnTeam(player) then
			local humanoid = character:FindFirstChildOfClass("Humanoid")
			if humanoid and humanoid.Health > 0 then -- debounce: only once
				humanoid.Health = 0
			end
		end
	end)
end
