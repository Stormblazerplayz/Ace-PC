--[[
	BUTTON BRIDGE (step on a button and a bridge appears for a few seconds)
	Great for obbies: hurry across before the bridge disappears!

	HOW TO USE:
	1. Make a Model (or Folder) in Workspace, for example called "BridgePuzzle".
	2. Inside it, put:
	   - a Part named exactly: Button
	   - a Folder named exactly: Bridge  (put all your bridge Parts inside this folder)
	3. Put this Script inside the same Model (next to Button and Bridge).
	All parts are made Anchored for you.
]]

local container = script.Parent
local button = container:WaitForChild("Button")
local bridgeFolder = container:WaitForChild("Bridge")

-- SETTINGS
local BRIDGE_TIME = 6 -- seconds the bridge stays
local OFF_COLOR = Color3.fromRGB(220, 60, 60) -- red button
local ON_COLOR = Color3.fromRGB(60, 220, 90) -- green button

button.Anchored = true
button.Color = OFF_COLOR

local function getBridgeParts()
	local parts = {}
	for _, object in bridgeFolder:GetDescendants() do
		if object:IsA("BasePart") then
			table.insert(parts, object)
		end
	end
	return parts
end

local function showBridge(visible)
	for _, part in getBridgeParts() do
		part.Anchored = true
		part.Transparency = visible and 0 or 0.85 -- a faint ghost so you can see where it will be
		part.CanCollide = visible
	end
end

showBridge(false)

local active = false -- debounce: true while the bridge is out

button.Touched:Connect(function(hit)
	if active then
		return
	end
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character or not character:FindFirstChildOfClass("Humanoid") then
		return
	end

	active = true
	button.Color = ON_COLOR
	showBridge(true)

	task.wait(BRIDGE_TIME)

	showBridge(false)
	button.Color = OFF_COLOR
	active = false
end)
