--[[
	SPINNING + BOBBING COINS (smooth animation)
	Makes every coin in the "Coins" folder spin and float up and down.
	It runs on each player's computer (a LocalScript), so it looks super smooth
	and doesn't lag the server.

	HOW TO USE:
	1. Have a Folder in Workspace named: Coins (with your coin Parts inside).
	   (Works great with the "coins" script.)
	2. Put this LocalScript in StarterPlayer > StarterPlayerScripts.
]]

local RunService = game:GetService("RunService")

local coinsFolder = workspace:WaitForChild("Coins")

-- SETTINGS
local SPIN_SPEED = 3 -- how fast the coins spin (bigger = faster)
local BOB_HEIGHT = 0.5 -- how many studs they float up and down
local BOB_SPEED = 2 -- how fast they float up and down

local startCFrames = {} -- remembers where each coin started

local function addCoin(object)
	if object:IsA("BasePart") then
		startCFrames[object] = object.CFrame
	end
end

local function removeCoin(object)
	startCFrames[object] = nil
end

for _, object in coinsFolder:GetDescendants() do
	addCoin(object)
end
coinsFolder.DescendantAdded:Connect(addCoin)
coinsFolder.DescendantRemoving:Connect(removeCoin)

RunService.RenderStepped:Connect(function()
	local time = os.clock()
	local bobOffset = math.sin(time * BOB_SPEED) * BOB_HEIGHT
	local spin = CFrame.Angles(0, time * SPIN_SPEED, 0)

	for coin, startCFrame in startCFrames do
		-- Start position + float up/down, then spin around the world's up direction,
		-- then keep the coin's own tilt (so standing-up coins stay standing up)
		local newPosition = startCFrame.Position + Vector3.new(0, bobOffset, 0)
		coin.CFrame = CFrame.new(newPosition) * spin * startCFrame.Rotation
	end
end)
