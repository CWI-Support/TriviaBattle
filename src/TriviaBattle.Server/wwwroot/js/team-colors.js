// Display colors for teams, by team order in the match (first team, second team...).
// Purely cosmetic: teams are chosen at the kiosk and the server doesn't care about colors.
const TEAM_COLORS = ['#ff2a3d', '#2a8cff', '#2ee66b', '#ffd400'];

function teamColor(match, teamId) {
    const index = match.teams.findIndex(t => t.id === teamId);
    return TEAM_COLORS[index % TEAM_COLORS.length] || TEAM_COLORS[0];
}
