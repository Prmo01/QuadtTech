// PDF Export functionality using jsPDF
window.pdfExport = {
    exportTableToPdf: function (tableId, title, filename) {
        // Check if jsPDF is already loaded (UMD build exposes it as window.jspdf)
        let jsPDF;
        if (typeof window.jspdf !== 'undefined' && window.jspdf.jsPDF) {
            jsPDF = window.jspdf.jsPDF;
        } else if (typeof window.jspdf !== 'undefined') {
            // Try direct access
            jsPDF = window.jspdf;
        } else {
            console.error('jsPDF library not loaded. Please ensure it is included in index.html');
            alert('PDF export failed: jsPDF library not loaded. Please refresh the page and try again.');
            return;
        }

        try {
            const doc = new jsPDF('l', 'mm', 'a4'); // Landscape for tables
            
            const table = document.getElementById(tableId);
            if (!table) {
                console.error('Table not found:', tableId);
                alert('Table not found. Please ensure the report table is visible.');
                return;
            }

            // Add title
            doc.setFontSize(16);
            doc.text(title, 145, 15, { align: 'center' });
            
            // Add date
            const now = new Date();
            doc.setFontSize(10);
            doc.text(`Generated: ${now.toLocaleString()}`, 145, 22, { align: 'center' });
            
            // Get table rows
            const rows = [];
            const headerRow = table.querySelector('thead tr');
            if (headerRow) {
                const headers = Array.from(headerRow.querySelectorAll('th')).map(th => th.textContent.trim());
                rows.push(headers);
            }
            
            const bodyRows = table.querySelectorAll('tbody tr');
            bodyRows.forEach(row => {
                const cells = Array.from(row.querySelectorAll('td')).map(td => {
                    // Get text content, handling nested elements
                    const text = td.textContent.trim();
                    return text;
                });
                rows.push(cells);
            });

            // Add footer rows if exists
            const footerRow = table.querySelector('tfoot tr');
            if (footerRow) {
                const footerCells = Array.from(footerRow.querySelectorAll('td')).map(td => td.textContent.trim());
                rows.push(footerCells);
            }
            
            if (rows.length === 0) {
                alert('No data to export.');
                return;
            }
            
            // Calculate column widths dynamically based on content
            const colCount = rows.length > 0 ? rows[0].length : 0;
            if (colCount === 0) {
                alert('No columns found in table.');
                return;
            }
            
            const availableWidth = 270; // A4 landscape width minus margins
            const colWidth = availableWidth / colCount;
            const fontSize = 9;
            
            let y = 35;
            const pageHeight = 190;
            
            rows.forEach((row, index) => {
                // Check if we need a new page
                if (y > pageHeight && index > 0) {
                    doc.addPage();
                    y = 20;
                }

                let x = 15;
                row.forEach((cell, cellIndex) => {
                    doc.setFontSize(index === 0 || index === rows.length - 1 ? fontSize + 1 : fontSize);
                    doc.setFont('helvetica', (index === 0 || index === rows.length - 1) ? 'bold' : 'normal');
                    
                    // Word wrap for long text
                    const maxWidth = colWidth - 2;
                    const lines = doc.splitTextToSize(cell || '', maxWidth);
                    const lineHeight = 5;
                    lines.forEach((line, lineIndex) => {
                        doc.text(line, x, y + (lineIndex * lineHeight), { maxWidth: maxWidth });
                    });
                    x += colWidth;
                });
                
                // Increase y position based on content height
                const maxLines = Math.max(...row.map(cell => {
                    const maxWidth = colWidth - 2;
                    const lines = doc.splitTextToSize(cell || '', maxWidth);
                    return lines.length;
                }));
                const cellHeight = (maxLines * 5) + 2;
                y += cellHeight;
            });
            
            doc.save(filename || 'report.pdf');
        } catch (error) {
            console.error('Error generating PDF:', error);
            alert('Error generating PDF: ' + error.message);
        }
    }
};
