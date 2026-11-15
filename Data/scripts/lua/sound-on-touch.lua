--[[
	SOUND ON TOUCH
	Plays a sound when a player touches the part (a squeak, a boing, a scream).

	HOW TO USE:
	1. Make a Part.
	2. Either put a Sound inside the Part (and set its SoundId),
	   OR type the sound's ID into SOUND_ID below and the script makes the Sound for you.
	3. Insert a Script into the Part and paste this in.
]]

local Players = game:GetService("Players")

local part = script.Parent

-- SETTINGS
local SOUND_ID = "" -- example: "rbxassetid://1234567890" (leave "" if you put a Sound in the part)
local VOLUME = 0.8
local COOLDOWN = 1 -- seconds before it can play again
local ONLY_PLAYERS = true -- false = anything with a Humanoid can make the sound

local sound = part:FindFirstChildOfClass("Sound")
if not sound then
	if SOUND_ID == "" then
		warn("sound-on-touch: put a Sound inside " .. part.Name .. " or set SOUND_ID in the script.")
		return
	end
	sound = Instance.new("Sound")
	sound.SoundId = SOUND_ID
	sound.Parent = part -- a Sound inside a Part is 3D: it's louder when you're close
end
sound.Volume = VOLUME

local onCooldown = false -- debounce

part.Touched:Connect(function(hit)
	if onCooldown then
		return
	end
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character or not character:FindFirstChildOfClass("Humanoid") then
		return
	end
	if ONLY_PLAYERS and not Players:GetPlayerFromCharacter(character) then
		return
	end

	onCooldown = true
	sound:Play()
	task.wait(COOLDOWN)
	onCooldown = false
end)
