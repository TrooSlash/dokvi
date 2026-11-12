(() => {
  const tables = [...document.querySelectorAll('#document table')];
  const [wide, compact, prose] = tables;
  const wrapper = wide.parentElement;
  const lineCount = element => {
    const range = document.createRange();
    range.selectNodeContents(element);
    return new Set([...range.getClientRects()].filter(r => r.width > 0).map(r => Math.round(r.top))).size;
  };
  const lineCounts = [...wide.querySelectorAll('th,td')].map(lineCount);
  const wideRightAligned = [...wide.querySelectorAll('th,td')].every(cell => getComputedStyle(cell).textAlign === 'right');
  wrapper.scrollLeft = wrapper.scrollWidth;
  const last = wide.rows[1].cells[11].getBoundingClientRect();
  const bounds = wrapper.getBoundingClientRect();
  const lastColumnReachable = wrapper.scrollLeft > 0 && last.right <= bounds.right + 1 && last.right > bounds.left;
  wrapper.scrollLeft = 0;
  const compactFits = compact.parentElement.scrollWidth <= compact.parentElement.clientWidth + 1;
  const proseWraps = lineCount(prose.rows[1].cells[0]) > 1;
  const explicitBreak = prose.rows[1].cells[1].querySelector('br') !== null && lineCount(prose.rows[1].cells[1]) > 1;
  const separators = getComputedStyle(wide.rows[1].cells[0]).borderRightWidth === '1px';
  const localOverflow = document.documentElement.scrollWidth <= innerWidth && document.getElementById('document').scrollWidth <= document.getElementById('document').clientWidth + 1;
  const expectedAlignment = ['left', 'center', 'right'];
  const alignment = [...compact.rows].map(row => [...row.cells].map(cell => getComputedStyle(cell).textAlign));
  const aligned = alignment.every(row => row.every((value, index) => value === expectedAlignment[index]));
  const textPositions = [...compact.rows].every(row => [...row.cells].every((cell, index) => {
    const range = document.createRange();
    range.selectNodeContents(cell);
    const text = range.getBoundingClientRect(), box = cell.getBoundingClientRect(), style = getComputedStyle(cell);
    const left = text.left - box.left, right = box.right - text.right;
    return index === 0 ? Math.abs(left - parseFloat(style.paddingLeft)) < 2
      : index === 1 ? Math.abs(left - right) < 2 : Math.abs(right - parseFloat(style.paddingRight)) < 2;
  }));
  const defaultLeft = [...prose.querySelectorAll('th,td')].every(cell => getComputedStyle(cell).textAlign === 'left');
  const raw = [...document.querySelectorAll('#document p')].find(p => p.textContent === 'HTML alignment probe');
  const rawHtmlClean = Boolean(raw && !['style','onclick','class','id'].some(name => raw.hasAttribute(name)));
  return {
    ok: lineCounts.every(count => count === 1) && wideRightAligned && lastColumnReachable && compactFits && proseWraps && explicitBreak && separators && localOverflow && aligned && textPositions && defaultLeft && rawHtmlClean,
    lineCounts, viewport: innerWidth, wrapperWidth: wrapper.clientWidth, tableWidth: wide.clientWidth,
    lastColumnReachable, compactFits, proseWraps, explicitBreak, separators, localOverflow, alignment, textPositions, defaultLeft, rawHtmlClean, wideRightAligned
  };
})()
