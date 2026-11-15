--[[
	KILL BRICKS FOLDER (one script for ALL your kill bricks)
	Instead of putting a script in every lava part, put all of them in one folder.

	HOW TO USE:
	1. In the Explorer, right-click Workspace > Insert Object > Folder.
	2. Rename the folder to: KillBricks
	3. Drag all your deadly parts into that folder (parts inside Models in it work too).
	4. Put this Script in ServerScriptService.
	New parts you add to the folder while the game runs will also be deadly.
]]

local killFolder = workspace:WaitForChild("KillBricks")

-- SETTINGS
local MAKE_THEM_GLOW = true -- turns every kill brick red + neon

local function onTouched(hit)
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character then
		return
	end
	local humanoid = character:FindFirstChildOfClass("Humanoid")
	-- Health > 0 check = debounce (we only kill once)
	if humanoid and humanoid.Health > 0 then
		humanoid.Health = 0
	end
end

local function setUpPart(object)
	if object:IsA("BasePart") then
		if MAKE_THEM_GLOW then
			object.Color = Color3.fromRGB(255, 40, 40)
			object.Material = Enum.Material.Neon
		end
		object.Touched:Connect(onTouched)
	end
end

-- Set up every part that is already in the folder
for _, object in killFolder:GetDescendants() do
	setUpPart(object)
end

-- And any part added later
killFolder.DescendantAdded:Connect(setUpPart)
