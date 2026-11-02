// The arcade lobby on the screens (Core/Arcade/Lobby.cs has the rules; the server sends state.lobby).
// A station not in a game shows where its player is:
//   Idle     "press any button to play" (and a call to join if a party or race is forming)
//   Hero     the heroes, one at a time: A and B flip through them, ACTION chooses
//   Game     the games on offer, laid out like a question (A, B, C), D to go back
//   Category the questions for a new game: everything mixed, or one category (A and B, ACTION, D back)
//   Waiting  the party or race forming: who's in (a challenged party: the two sides, VS), who's ready, when it starts
// The main screen shows the whole room's lobby under its title (ArcadeLobby.room).
// Hero pictures are in media/heroes (drawn by /dev/heroes). Needs ui.js and theme.js. Styles: css/arcade-lobby.css.

const ArcadeLobby = {
    /** The station screen's lobby, for this station (state.station). */
    station(state) {
        const lobby = state.lobby;
        const seat = lobby.seats.find(s => s.stationId === state.station.id);
        const seed = `${state.station.id}:${seat?.name}`;
        switch (seat?.step) {
            case 'Hero': return ArcadeLobby.heroStep(lobby, seat, seed);
            case 'Game': return ArcadeLobby.gameStep(lobby, seat);
            case 'Category': return ArcadeLobby.categoryStep(lobby, seat);
            case 'Waiting': return ArcadeLobby.waitingStep(lobby, seat);
            default: return ArcadeLobby.idleStep(lobby);
        }
    },

    idleStep(lobby) {
        const forming = lobby.forming;
        const call = forming
            ? `<div class="lb-call">${ArcadeLobby.joinCall(forming)} <b data-ends-at="${forming.startsAtMs}"></b></div>`
            : '';
        return `
            <div class="lb lb-idle">
                <h1 class="big-title lb-logo">${Theme.text('brand.title')}</h1>
                <div class="lb-lineup">${lobby.heroes.map((hero, i) => `<img src="${ArcadeLobby.picture(hero)}" alt="" style="--i: ${i}">`).join('')}</div>
                ${call}
                <div class="lb-press">${Theme.text('arcade.pressAny')}</div>
                <div class="lb-swipe">${Theme.text('arcade.orSwipe')}</div>
            </div>`;
    },

    heroStep(lobby, seat, seed) {
        const dots = lobby.heroes.map(hero => `<i class="${hero === seat.hero ? 'is-on' : ''}"></i>`).join('');
        return `
            <div class="lb lb-hero">
                <div class="lb-hello">${Theme.text('arcade.hello', { name: seat.name }, seed)}</div>
                <h1 class="lb-title">${Theme.text('arcade.chooseHero')}</h1>
                <div class="lb-carousel">
                    <div class="lb-turn" data-letter="A"><span class="answer-letter">A</span>&#9664;</div>
                    <img class="lb-card" src="${ArcadeLobby.picture(seat.hero)}" alt="">
                    <div class="lb-turn" data-letter="B">&#9654;<span class="answer-letter">B</span></div>
                </div>
                <div class="lb-hero-name">${ArcadeLobby.heroName(seat.hero)}</div>
                <div class="lb-hero-line">${ArcadeLobby.heroLine(seat.hero)}</div>
                <div class="lb-dots">${dots}</div>
                <div class="lb-keys">${Theme.text('arcade.heroKeys')}</div>
            </div>`;
    },

    gameStep(lobby, seat) {
        const option = (letter, title, line) => `
            <div class="answer lb-option" data-letter="${letter}">
                <span class="answer-letter">${letter}</span>
                <span><b>${title}</b><small>${line}</small></span>
            </div>`;
        const forming = lobby.forming;
        const choices = seat.choices.map((choice, i) => {
            const letter = UI.ANSWER_LETTERS[i];
            switch (choice) {
                case 'Solo': return option(letter, Theme.text('arcade.solo'), Theme.text('arcade.soloLine'));
                case 'Party': return option(letter, Theme.text('arcade.party'), Theme.text('arcade.partyLine'));
                case 'Race': return option(letter, Theme.text('arcade.race'), Theme.text('arcade.raceLine'));
                case 'Join': return option(letter,
                    forming.kind === 'Race' ? Theme.text('arcade.joinRace', { name: forming.hostName }) : Theme.text('arcade.joinParty', { name: forming.hostName }),
                    Theme.text('arcade.joinLine', { category: Theme.raw(ArcadeLobby.categoryName(forming.category)), count: forming.members.length }));
                case 'Challenge': return option(letter, Theme.text('arcade.challenge', { name: forming.hostName }), Theme.text('arcade.challengeLine'));
                default: return '';
            }
        }).join('');
        return `
            <div class="lb lb-game">
                <div class="lb-me">
                    <img src="${ArcadeLobby.picture(seat.hero, 'head')}" alt="">
                    <span>${ArcadeLobby.heroName(seat.hero)}</span>
                </div>
                <h1 class="lb-title">${Theme.text('arcade.chooseGame', { name: seat.name })}</h1>
                <div class="answer-grid lb-options">
                    ${choices}
                    ${option('D', Theme.text('arcade.back'), Theme.text('arcade.backLine'))}
                </div>
            </div>`;
    },

    categoryStep(lobby, seat) {
        const names = [null, ...lobby.categories];
        const game = seat.forGame === 'Race' ? Theme.text('arcade.race') : seat.forGame === 'Party' ? Theme.text('arcade.party') : Theme.text('arcade.solo');
        const list = names.map((name, i) => `
            <div class="lb-category ${i === seat.categoryIndex ? 'is-on' : ''}">${ArcadeLobby.categoryName(name)}</div>`).join('');
        return `
            <div class="lb lb-categories">
                <div class="lb-me">
                    <img src="${ArcadeLobby.picture(seat.hero, 'head')}" alt="">
                    <span>${game}</span>
                </div>
                <h1 class="lb-title">${Theme.text('arcade.chooseCategory')}</h1>
                <div class="lb-category-list">${list}</div>
                <div class="lb-keys">${Theme.text('arcade.categoryKeys')}</div>
            </div>`;
    },

    /** A category's name, HTML-escaped; null is everything mixed. */
    categoryName(name) {
        return name ? UI.escape(name) : Theme.text('arcade.everything');
    },

    waitingStep(lobby, seat) {
        const group = lobby.forming;
        if (!group) return ArcadeLobby.idleStep(lobby); // it started (or closed) a moment ago
        const title = group.kind === 'Race' ? Theme.text('arcade.raceTitle', { name: group.hostName })
            : group.isBattle ? Theme.text('arcade.battleTitle')
            : Theme.text('arcade.partyTitle', { name: group.hostName });
        const member = m => `
            <div class="lb-member ${m.ready ? 'is-ready' : ''} ${m.stationId === seat.stationId ? 'is-me' : ''}">
                <img src="${ArcadeLobby.picture(m.hero, 'head')}" alt="">
                <b>${UI.escape(m.name)}</b>
                <small>${Theme.text(m.ready ? 'arcade.ready' : 'arcade.notReady')}</small>
            </div>`;
        const side = n => group.members.filter(m => m.side === n).map(member).join('');
        const members = group.isBattle
            ? `<div class="lb-side">${side(0)}</div><div class="lb-vs">${Theme.text('arcade.vs')}</div><div class="lb-side">${side(1)}</div>`
            : group.members.map(member).join('');
        return `
            <div class="lb lb-waiting">
                <h1 class="lb-title">${title}</h1>
                <div class="lb-subject">${ArcadeLobby.categoryName(group.category)}</div>
                <div class="lb-starts"><small>${Theme.text('arcade.startsIn')}</small><b data-ends-at="${group.startsAtMs}"></b></div>
                <div class="lb-members ${group.isBattle ? 'is-battle' : ''}">${members}</div>
                <div class="lb-press">${Theme.text(seat.ready ? 'arcade.waitingForOthers' : 'arcade.pressReady')}</div>
                <div class="lb-keys">${Theme.text('arcade.leave')}</div>
            </div>`;
    },

    /** The main screen, under its title: every station of the room, and the party forming. */
    room(state) {
        const lobby = state.lobby;
        const label = id => UI.escape(state.room.stations.find(s => s.id === id)?.label || id);
        const seats = lobby.seats.map(seat => {
            const busy = seat.step !== 'Idle';
            const status = !busy ? Theme.text('arcade.roomFree')
                : seat.step === 'Waiting' ? Theme.text('arcade.roomWaiting', { name: seat.name })
                : Theme.text('arcade.roomChoosing', { name: seat.name });
            return `
                <div class="lb-room-seat ${busy ? 'is-busy' : ''}">
                    ${busy ? `<img src="${ArcadeLobby.picture(seat.hero, 'head')}" alt="">` : ''}
                    <small>${label(seat.stationId)}</small>
                    <span>${status}</span>
                </div>`;
        }).join('');
        const call = lobby.forming ? `<div class="lb-call">${ArcadeLobby.joinCall(lobby.forming)} <b data-ends-at="${lobby.forming.startsAtMs}"></b></div>` : '';
        return `<div class="lb-room">${call}<div class="lb-room-seats">${seats}</div></div>`;
    },

    joinCall(group) {
        return group.kind === 'Race'
            ? Theme.text('arcade.joinCallRace', { name: group.hostName })
            : Theme.text('arcade.joinCallParty', { name: group.hostName });
    },

    /** A hero's picture: 'body' (the hero card) or 'head'. Hero keys come from the server's list. */
    picture(hero, framing = 'body') {
        return `/media/heroes/${encodeURIComponent(hero)}${framing === 'head' ? '-head' : ''}.png`;
    },

    /** The hero's name and line, in the theme's words. Literal keys, so ThemeFilesTests can check them. */
    heroName(hero) {
        switch (hero) {
            case 'knight': return Theme.text('heroes.knight');
            case 'barbarian': return Theme.text('heroes.barbarian');
            case 'mage': return Theme.text('heroes.mage');
            case 'rogue': return Theme.text('heroes.rogue');
            case 'ranger': return Theme.text('heroes.ranger');
            default: return UI.escape(hero);
        }
    },

    heroLine(hero) {
        switch (hero) {
            case 'knight': return Theme.text('heroes.knightLine');
            case 'barbarian': return Theme.text('heroes.barbarianLine');
            case 'mage': return Theme.text('heroes.mageLine');
            case 'rogue': return Theme.text('heroes.rogueLine');
            case 'ranger': return Theme.text('heroes.rangerLine');
            default: return '';
        }
    },
};
