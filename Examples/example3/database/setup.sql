-- The to-do list's database, WebLingoTodo, on SQL Server Express LocalDB: its lists, and a few dozen items on them, due
-- around the day it is run. Run it again to start afresh: it drops the database first. From this folder, either
--
--   weblingo run setup.wl
--
-- which runs it through WebLingo's std:db, or, where SQL Server's own tools are installed,
--
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -i setup.sql
--
-- Each batch ends with GO. The scripting variable Database is the database's name, which setup.wl's parameter
-- database=Name changes.

:setvar Database WebLingoTodo

IF DB_ID(N'$(Database)') IS NOT NULL
BEGIN
    ALTER DATABASE [$(Database)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$(Database)];
END
GO

CREATE DATABASE [$(Database)];
GO

USE [$(Database)];
GO

CREATE TABLE Lists (
    Id INT IDENTITY PRIMARY KEY,
    Name NVARCHAR(40) NOT NULL UNIQUE,
    Colour CHAR(7) NOT NULL
);

CREATE TABLE Items (
    Id INT IDENTITY PRIMARY KEY,
    ListId INT NOT NULL REFERENCES Lists (Id),
    Title NVARCHAR(200) NOT NULL,
    Notes NVARCHAR(1000) NULL,
    Priority TINYINT NOT NULL DEFAULT 2 CHECK (Priority BETWEEN 1 AND 3), -- 1 high, 2 normal, 3 low
    Due DATE NULL,
    Done BIT NOT NULL DEFAULT 0,
    Created DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    Completed DATETIME2(0) NULL
);

CREATE INDEX IX_Items_Open ON Items (Done, Due);
GO

INSERT INTO Lists (Name, Colour) VALUES
    (N'Home', '#2f7d6d'),
    (N'Work', '#3a5ea8'),
    (N'Garden', '#6b8e23'),
    (N'Shopping', '#b5651d');

-- Each item's due date is a number of days from today, so that some are overdue and some to come, whenever this runs.
DECLARE @today DATE = CAST(SYSDATETIME() AS DATE);

INSERT INTO Items (ListId, Title, Notes, Priority, Due, Done, Completed)
SELECT l.Id, i.Title, i.Notes, i.Priority, DATEADD(DAY, i.Days, @today), i.Done,
       CASE WHEN i.Done = 1 THEN DATEADD(HOUR, 18, CAST(DATEADD(DAY, ISNULL(i.Days, 0) - 1, @today) AS DATETIME2(0))) END
FROM (VALUES
    (N'Home', N'Pay the electricity bill', N'Online, before the reminder comes.', 1, -2, 0),
    (N'Home', N'Book the boiler''s service', NULL, 2, 5, 0),
    (N'Home', N'Fix the dripping kitchen tap', N'A new washer, 15 mm.', 2, 1, 0),
    (N'Home', N'Change the smoke alarm''s battery', NULL, 1, -1, 1),
    (N'Home', N'Return the library books', N'Three of them, on the hall table.', 2, 3, 0),
    (N'Home', N'Call Grandma', N'Her birthday is on Sunday.', 1, 4, 0),
    (N'Home', N'Defrost the freezer', NULL, 3, 14, 0),
    (N'Home', N'Renew the car''s insurance', N'Compare two quotes first.', 1, 9, 0),
    (N'Home', N'Sort the photos from the summer', NULL, 3, NULL, 0),
    (N'Work', N'Send the November invoice', NULL, 1, 0, 0),
    (N'Work', N'Review Ana''s pull request', N'The one about the date formats.', 2, 1, 0),
    (N'Work', N'Prepare the slides for Thursday', N'Ten slides at most; the numbers from the report.', 1, 2, 0),
    (N'Work', N'Answer the client''s questions', NULL, 2, -3, 1),
    (N'Work', N'Book the train to the conference', N'The early one, with a seat reserved.', 2, 6, 0),
    (N'Work', N'Write up the meeting''s notes', NULL, 3, -1, 1),
    (N'Work', N'Update the project''s plan', NULL, 2, 7, 0),
    (N'Work', N'Renew the domain name', N'It expires at the end of the month.', 1, 12, 0),
    (N'Work', N'Clear out the old branches', NULL, 3, NULL, 0),
    (N'Garden', N'Plant the tulip bulbs', N'Fifteen centimetres deep, pointed end up.', 2, 2, 0),
    (N'Garden', N'Rake the leaves on the lawn', NULL, 3, -4, 1),
    (N'Garden', N'Prune the apple tree', N'Once its leaves have fallen.', 2, 20, 0),
    (N'Garden', N'Empty and store the hosepipe', N'Before the first frost.', 1, 3, 0),
    (N'Garden', N'Clean the greenhouse''s glass', NULL, 3, 10, 0),
    (N'Garden', N'Mulch the roses', N'Two bags of bark.', 2, -2, 0),
    (N'Garden', N'Sharpen the mower''s blade', NULL, 3, NULL, 0),
    (N'Garden', N'Fill the bird feeders', NULL, 2, 0, 1),
    (N'Garden', N'Turn the compost heap', NULL, 3, 8, 0),
    (N'Shopping', N'Milk, bread and eggs', NULL, 1, 0, 0),
    (N'Shopping', N'Batteries for the remote', N'Two AAA.', 3, NULL, 0),
    (N'Shopping', N'A present for Leo''s birthday', N'He likes puzzles and dinosaurs.', 1, 5, 0),
    (N'Shopping', N'Coffee beans', NULL, 2, 1, 1),
    (N'Shopping', N'Winter tyres', N'Ask the garage about the fitting.', 2, 15, 0),
    (N'Shopping', N'Light bulbs for the hall', N'E27, warm white.', 3, 4, 0),
    (N'Shopping', N'Seeds for the spring', NULL, 3, 30, 0),
    (N'Shopping', N'Stamps', NULL, 3, -1, 0),
    (N'Shopping', N'Dog food', N'The large bag.', 2, 2, 0)
) AS i (List, Title, Notes, Priority, Days, Done)
JOIN Lists l ON l.Name = i.List;

SELECT (SELECT COUNT(*) FROM Lists) AS Lists, (SELECT COUNT(*) FROM Items) AS Items,
       (SELECT COUNT(*) FROM Items WHERE Done = 0) AS [Open];
GO
