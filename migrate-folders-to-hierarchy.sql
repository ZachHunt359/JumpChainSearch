-- Data Migration: Move SB Drive subfolders from DriveConfigurations to FolderConfigurations
-- Run this after applying AddFolderConfigurationsTable migration

BEGIN TRANSACTION;

-- Step 1: Insert SB Drive subfolders into FolderConfigurations
INSERT INTO FolderConfigurations (
    FolderId,
    FolderName,
    ParentDriveId,
    ResourceKey,
    PreferredAuthMethod,
    FolderPath,
    DocumentCount,
    LastScanTime,
    IsActive,
    Description,
    IsAutoDiscovered,
    CreatedAt,
    UpdatedAt
)
SELECT 
    dc.DriveId as FolderId,
    dc.DriveName as FolderName,
    4 as ParentDriveId, -- SB Drive has Id = 4
    dc.ResourceKey,
    dc.PreferredAuthMethod,
    '/SB Drive/' || dc.DriveName as FolderPath,
    dc.DocumentCount,
    dc.LastScanTime,
    dc.IsActive,
    dc.Description,
    0 as IsAutoDiscovered, -- These were manually configured
    datetime('now') as CreatedAt,
    datetime('now') as UpdatedAt
FROM DriveConfigurations dc
WHERE dc.ParentDriveName = 'SB Drive'
   OR dc.DriveName = 'SB Uploads For New Jumps'; -- This should also be under SB Drive

-- Step 2: Delete the subfolder entries from DriveConfigurations
DELETE FROM DriveConfigurations 
WHERE ParentDriveName = 'SB Drive'
   OR DriveName = 'SB Uploads For New Jumps';

-- Step 3: Verify we now have exactly 10 drives
SELECT 'Drive Count Check:' as Check, COUNT(*) as Count FROM DriveConfigurations WHERE IsActive = 1;

-- Step 4: Verify we have 12 folders under SB Drive
SELECT 'Folder Count Check:' as Check, COUNT(*) as Count FROM FolderConfigurations WHERE ParentDriveId = 4;

-- Step 5: Show final drive list
SELECT 'Final Drives:' as Info, Id, DriveName FROM DriveConfigurations WHERE IsActive = 1 ORDER BY DriveName;

-- Step 6: Show SB Drive folders
SELECT 'SB Drive Folders:' as Info, Id, FolderName, DocumentCount FROM FolderConfigurations WHERE ParentDriveId = 4 ORDER BY FolderName;

COMMIT;